using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SmartFactory.Api.Data;
using SmartFactory.Api.Exceptions;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Models.Entities;
using SmartFactory.Api.Services;
using SmartFactory.Tests.Helpers;
using Xunit;

namespace SmartFactory.Tests.Unit;

public class NcrServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CrashingDbContext _context;
    private readonly IFileStorageService _fileStorageMock;
    private readonly NcrService _sut;

    public class CrashingDbContext : FactoryDbContext
    {
        public bool ShouldFailOnSave { get; set; }

        public CrashingDbContext(DbContextOptions<FactoryDbContext> options) : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ShouldFailOnSave)
            {
                throw new DbUpdateException("Simulated database failure during transaction execution.");
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    public NcrServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<FactoryDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new CrashingDbContext(options);
        _context.Database.EnsureCreated();

        _fileStorageMock = Substitute.For<IFileStorageService>();
        _sut = new NcrService(_context, _fileStorageMock);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    [Theory]
    [InlineData("Critical")]
    [InlineData("Major")]
    public async Task CreateInspectionReportAsync_WithSevereDefect_LocksProductionLot(string severity)
    {
        // Arrange
        var request = new NcrInspectionRequest
        {
            LotId = 1,
            StationId = 1,
            ReportedByUserId = 2,
            DefectType = "Crack",
            Severity = severity,
            Description = "Structural crack detected on casing"
        };

        // Act
        var result = await _sut.CreateInspectionReportAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Severity.Should().Be(severity);
        result.LotStatus.Should().Be("Locked");

        var lot = await _context.ProductionLots.FindAsync(1);
        lot!.Status.Should().Be("Locked");
        lot.DefectQuantity.Should().Be(1);
    }

    [Fact]
    public async Task CreateInspectionReportAsync_WithMinorDefect_KeepsLotInProgress()
    {
        // Arrange
        var request = new NcrInspectionRequest
        {
            LotId = 1,
            StationId = 1,
            ReportedByUserId = 2,
            DefectType = "Scratch",
            Severity = "Minor",
            Description = "Minor surface scratch"
        };

        // Act
        var result = await _sut.CreateInspectionReportAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.LotStatus.Should().Be("InProgress");

        var lot = await _context.ProductionLots.FindAsync(1);
        lot!.Status.Should().Be("InProgress");
        lot.DefectQuantity.Should().Be(1);
    }

    [Fact]
    public async Task CreateInspectionReportAsync_WhenDatabaseFails_PerformsCompensatingCleanupOnPhysicalFile()
    {
        // Arrange
        var bytes = TestFileHelper.CreateValidJpegBytes();
        var file = TestFileHelper.CreateFormFile(bytes, "defect.jpg", "image/jpeg");
        const string fakeSavedUrl = "/uploads/defects/defect_abc.jpg";

        _fileStorageMock.SaveFileAsync(Arg.Any<IFormFile>(), "defects", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(fakeSavedUrl));

        _context.ShouldFailOnSave = true; // Trigger failure during DB commit

        var request = new NcrInspectionRequest
        {
            LotId = 1,
            StationId = 1,
            ReportedByUserId = 2,
            DefectType = "Deformation",
            Severity = "Major",
            Description = "Deformation test for rollback",
            Image = file
        };

        // Act
        var act = () => _sut.CreateInspectionReportAsync(request);

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();

        // Verify that compensating cleanup was executed
        _fileStorageMock.Received(1).DeleteFile(fakeSavedUrl);

        // Verify that lot was rolled back and not locked
        _context.ShouldFailOnSave = false;
        var lot = await _context.ProductionLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == 1);
        lot!.Status.Should().Be("InProgress");
    }

    [Fact]
    public async Task CreateInspectionReportAsync_WithNonExistentLot_ThrowsNotFoundException()
    {
        // Arrange
        var request = new NcrInspectionRequest
        {
            LotId = 9999, // Non-existent
            StationId = 1,
            ReportedByUserId = 2,
            DefectType = "Scratch",
            Severity = "Minor",
            Description = "Test"
        };

        // Act
        var act = () => _sut.CreateInspectionReportAsync(request);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Production lot with ID 9999 not found.*");
    }

    [Fact]
    public async Task ProcessDecisionAsync_WithRework_UnlocksLotToInProgressAndResolvesNcr()
    {
        // Arrange: first create a locked lot via a Major defect
        var inspectRequest = new NcrInspectionRequest
        {
            LotId = 1,
            StationId = 1,
            ReportedByUserId = 2,
            DefectType = "Crack",
            Severity = "Major",
            Description = "Test crack"
        };
        var inspectResult = await _sut.CreateInspectionReportAsync(inspectRequest);

        var decisionRequest = new NcrDecisionRequest
        {
            NcrReportId = inspectResult.Id,
            Decision = "Rework",
            Notes = "Sent to rework station for correction",
            ApprovedByUserId = 1
        };

        // Act
        var decisionResult = await _sut.ProcessDecisionAsync(decisionRequest);

        // Assert
        decisionResult.Should().NotBeNull();
        decisionResult.Decision.Should().Be("Rework");
        decisionResult.ProductionLotStatus.Should().Be("InProgress");
        decisionResult.NcrReportStatus.Should().Be("Resolved");

        var lot = await _context.ProductionLots.FindAsync(1);
        lot!.Status.Should().Be("InProgress");

        var ncr = await _context.NcrReports.FindAsync(inspectResult.Id);
        ncr!.Status.Should().Be("Resolved");
    }

    [Fact]
    public async Task ProcessDecisionAsync_OnAlreadyResolvedNcr_ThrowsConflictException()
    {
        // Arrange: create inspection and resolve it once
        var inspectResult = await _sut.CreateInspectionReportAsync(new NcrInspectionRequest
        {
            LotId = 1,
            StationId = 1,
            ReportedByUserId = 2,
            DefectType = "Scratch",
            Severity = "Minor",
            Description = "Initial defect"
        });

        await _sut.ProcessDecisionAsync(new NcrDecisionRequest
        {
            NcrReportId = inspectResult.Id,
            Decision = "Rework",
            ApprovedByUserId = 1
        });

        // Act: attempt to resolve again
        var secondDecision = () => _sut.ProcessDecisionAsync(new NcrDecisionRequest
        {
            NcrReportId = inspectResult.Id,
            Decision = "Scrap",
            ApprovedByUserId = 1
        });

        // Assert
        await secondDecision.Should().ThrowAsync<ConflictException>()
            .WithMessage("*has already been resolved*");
    }

    [Fact]
    public async Task ProcessDecisionAsync_WithNonExistentNcr_ThrowsNotFoundException()
    {
        // Arrange
        var request = new NcrDecisionRequest
        {
            NcrReportId = 8888,
            Decision = "Rework",
            ApprovedByUserId = 1
        };

        // Act
        var act = () => _sut.ProcessDecisionAsync(request);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*NCR Report with ID 8888 not found.*");
    }

    [Fact]
    public async Task CreateInspectionReportAsync_WithNonExistentStation_ThrowsNotFoundException()
    {
        // Arrange
        var request = new NcrInspectionRequest
        {
            LotId = 1,
            StationId = 9999, // Non-existent station
            ReportedByUserId = 2,
            DefectType = "Crack",
            Severity = "Minor",
            Description = "Station test"
        };

        // Act
        var act = () => _sut.CreateInspectionReportAsync(request);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Work station with ID 9999 not found.*");
    }

    [Fact]
    public async Task CreateInspectionReportAsync_WithNonExistentReportedByUser_ThrowsNotFoundException()
    {
        // Arrange
        var request = new NcrInspectionRequest
        {
            LotId = 1,
            StationId = 1,
            ReportedByUserId = 9999, // Non-existent user
            DefectType = "Crack",
            Severity = "Minor",
            Description = "User test"
        };

        // Act
        var act = () => _sut.CreateInspectionReportAsync(request);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*User with ID 9999 not found.*");
    }

    [Fact]
    public async Task ProcessDecisionAsync_WithNonExistentApprovedByUser_ThrowsNotFoundException()
    {
        // Arrange: create a valid NCR first
        var inspectResult = await _sut.CreateInspectionReportAsync(new NcrInspectionRequest
        {
            LotId = 1,
            StationId = 1,
            ReportedByUserId = 2,
            DefectType = "Crack",
            Severity = "Major",
            Description = "Approval user test"
        });

        var request = new NcrDecisionRequest
        {
            NcrReportId = inspectResult.Id,
            Decision = "Rework",
            ApprovedByUserId = 9999 // Non-existent approver
        };

        // Act
        var act = () => _sut.ProcessDecisionAsync(request);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*User with ID 9999 not found.*");
    }

    [Fact]
    public async Task CreateInspectionReportAsync_OnAlreadyLockedLot_WithMinorDefect_PreservesLockedStatus_AndIncrementsDefectQuantity()
    {
        // Arrange: 1. Lock the lot first via Major defect
        var majorRequest = new NcrInspectionRequest
        {
            LotId = 1,
            StationId = 1,
            ReportedByUserId = 2,
            DefectType = "Crack",
            Severity = "Major",
            Description = "Initial severe defect"
        };
        await _sut.CreateInspectionReportAsync(majorRequest);

        var lotBefore = await _context.ProductionLots.FindAsync(1);
        lotBefore!.Status.Should().Be("Locked");
        var initialDefectQty = lotBefore.DefectQuantity;

        // Act: 2. Submit a Minor defect on the already Locked lot
        var minorRequest = new NcrInspectionRequest
        {
            LotId = 1,
            StationId = 1,
            ReportedByUserId = 2,
            DefectType = "Scratch",
            Severity = "Minor",
            Description = "Subsequent minor defect on locked lot"
        };
        var minorResult = await _sut.CreateInspectionReportAsync(minorRequest);

        // Assert: 3. Status must NOT revert to InProgress, must remain Locked
        minorResult.LotStatus.Should().Be("Locked");

        var lotAfter = await _context.ProductionLots.FindAsync(1);
        lotAfter!.Status.Should().Be("Locked", "A minor defect must never unlock or revert a Locked lot.");
        lotAfter.DefectQuantity.Should().Be(initialDefectQty + 1);
    }

    [Theory]
    [InlineData("rework", "InProgress")]
    [InlineData("SCRAP", "Scrapped")]
    [InlineData("concession", "Released")]
    [InlineData("RETURN", "Released")]
    public async Task ProcessDecisionAsync_WithCaseInsensitiveDecision_MapsLotStatusCorrectly(string decisionInput, string expectedLotStatus)
    {
        // Arrange
        var inspectResult = await _sut.CreateInspectionReportAsync(new NcrInspectionRequest
        {
            LotId = 2,
            StationId = 2,
            ReportedByUserId = 2,
            DefectType = "Deformation",
            Severity = "Major",
            Description = "Case sensitivity test"
        });

        var decisionRequest = new NcrDecisionRequest
        {
            NcrReportId = inspectResult.Id,
            Decision = decisionInput,
            Notes = "Case sensitivity test note",
            ApprovedByUserId = 1
        };

        // Act
        var result = await _sut.ProcessDecisionAsync(decisionRequest);

        // Assert
        result.ProductionLotStatus.Should().Be(expectedLotStatus);
        var lot = await _context.ProductionLots.FindAsync(2);
        lot!.Status.Should().Be(expectedLotStatus);
    }

    [Fact]
    public async Task ProcessDecisionAsync_WhenUserHasKcsRole_ThrowsConflictException()
    {
        // Arrange: Tạo NCR
        var inspectResult = await _sut.CreateInspectionReportAsync(new NcrInspectionRequest
        {
            LotId = 1,
            StationId = 1,
            ReportedByUserId = 2,
            DefectType = "Pore",
            Severity = "Major",
            Description = "Test porosity"
        });

        // Act: KCS User (Id = 2) cố gắng phê duyệt
        var decisionRequest = new NcrDecisionRequest
        {
            NcrReportId = inspectResult.Id,
            Decision = "Rework",
            Notes = "KCS unauthorized attempt",
            ApprovedByUserId = 2 // Lê Thị KCS (Role: KCS)
        };

        // Assert: Ném ConflictException
        var act = async () => await _sut.ProcessDecisionAsync(decisionRequest);
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*không có thẩm quyền phê duyệt*");
    }

    [Fact]
    public async Task ProcessDecisionAsync_WhenUserHasManagerRole_Succeeds()
    {
        // Arrange: Tạo NCR
        var inspectResult = await _sut.CreateInspectionReportAsync(new NcrInspectionRequest
        {
            LotId = 1,
            StationId = 1,
            ReportedByUserId = 2,
            DefectType = "Pore",
            Severity = "Major",
            Description = "Test porosity"
        });

        // Act: Manager User (Id = 3) phê duyệt
        var decisionRequest = new NcrDecisionRequest
        {
            NcrReportId = inspectResult.Id,
            Decision = "Rework",
            Notes = "Manager approved rework",
            ApprovedByUserId = 3 // Vũ Đình Giám Đốc (Role: Manager)
        };

        var result = await _sut.ProcessDecisionAsync(decisionRequest);

        // Assert: Thành công
        result.Should().NotBeNull();
        result.Decision.Should().Be("Rework");
        result.ApprovedByName.Should().Be("Vũ Đình Giám Đốc");
    }
}
