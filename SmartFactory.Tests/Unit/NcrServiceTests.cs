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
}
