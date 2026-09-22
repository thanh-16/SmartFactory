using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Tests.Fixtures;
using SmartFactory.Tests.Helpers;
using Xunit;

namespace SmartFactory.Tests.Integration;

public class NcrInspectionIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public NcrInspectionIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostInspect_WithFakeExeDisguisedAsJpg_Returns400ProblemDetails()
    {
        // Arrange
        var fakeExeBytes = TestFileHelper.CreateFakeExeBytes();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Inspection with fake .exe"), "Description");

        var fileContent = new ByteArrayContent(fakeExeBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "Image", "malicious.jpg");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Invalid File Format");
        problem.Detail.Should().Contain("Executable files (.exe) are strictly prohibited");
    }

    [Fact]
    public async Task PostInspect_WithZeroByteFile_Returns400ProblemDetails()
    {
        // Arrange
        var zeroBytes = TestFileHelper.CreateZeroBytes();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Scratch"), "DefectType");
        form.Add(new StringContent("Minor"), "Severity");
        form.Add(new StringContent("Zero byte test"), "Description");

        var fileContent = new ByteArrayContent(zeroBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "Image", "empty.jpg");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Invalid File Format");
        problem.Detail.Should().Contain("File is empty or zero bytes");
    }

    [Fact]
    public async Task PostInspect_WithOver5MbFile_Returns400Or413ProblemDetails()
    {
        // Arrange
        var largeBytes = TestFileHelper.CreateOver5MbBytes();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Deformation"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Large file test"), "Description");

        var fileContent = new ByteArrayContent(largeBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "Image", "toolarge.jpg");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Payload Too Large");
        problem.Detail.Should().Contain("exceeds the maximum allowed limit of 5242880 bytes (5MB)");
    }

    [Fact]
    public async Task PostInspect_WithMajorDefect_Returns201CreatedAndLocksProductionLot()
    {
        // Arrange
        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Major crack detected during quality gate"), "Description");

        var fileContent = new ByteArrayContent(jpegBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "Image", "valid_crack.jpg");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var report = await response.Content.ReadFromJsonAsync<NcrReportResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        report.Should().NotBeNull();
        report!.Severity.Should().Be("Major");
        report.LotStatus.Should().Be("Locked");
        report.ImageUrls.Should().NotBeEmpty();

        // Verify database state directly
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var lot = await db.ProductionLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == 1);
        lot!.Status.Should().Be("Locked");
    }

    [Fact]
    public async Task PostDecision_WithRework_Returns201CreatedAndUnlocksLotToInProgress()
    {
        // Arrange: Step 1 - Create an inspection report with Major severity on Lot 2 to lock it
        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("2"), "LotId");
        form.Add(new StringContent("2"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Deformation"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Flange deformation requiring rework"), "Description");

        var fileContent = new ByteArrayContent(jpegBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "Image", "deformation.jpg");

        var inspectResponse = await _client.PostAsync("/api/ncr-reports/inspect", form);
        inspectResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var report = await inspectResponse.Content.ReadFromJsonAsync<NcrReportResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        report.Should().NotBeNull();

        // Step 2: Send Foreman Rework decision
        var decisionRequest = new NcrDecisionRequest
        {
            NcrReportId = report!.Id,
            Decision = "Rework",
            Notes = "Re-align flange on hydraulic press",
            ApprovedByUserId = 1
        };

        // Act
        var decisionResponse = await _client.PostAsJsonAsync("/api/ncr-decisions", decisionRequest);

        // Assert
        decisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var decision = await decisionResponse.Content.ReadFromJsonAsync<NcrDecisionResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        decision.Should().NotBeNull();
        decision!.Decision.Should().Be("Rework");
        decision.ProductionLotStatus.Should().Be("InProgress");
        decision.NcrReportStatus.Should().Be("Resolved");

        // Verify database state: lot must be unlocked to "InProgress"
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var lot = await db.ProductionLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == 2);
        lot!.Status.Should().Be("InProgress");
    }

    [Fact]
    public async Task PostInspect_WhenDatabaseCommitFails_DeletesPhysicalUploadedFile()
    {
        // Arrange: Create a dedicated factory instance with DbCrashInterceptor to trigger DB commit failure
        var interceptor = new DbCrashInterceptor { TriggerFailure = true };
        var crashConnection = new SqliteConnection("DataSource=:memory:");
        crashConnection.Open();

        using var crashingFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<FactoryDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<FactoryDbContext>(options =>
                {
                    options.UseSqlite(crashConnection);
                    options.AddInterceptors(interceptor);
                });

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
                db.Database.EnsureCreated();
            });
        });

        var client = crashingFactory.CreateClient();

        // Count existing files before test
        var uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "defects");
        if (!Directory.Exists(uploadDir))
        {
            Directory.CreateDirectory(uploadDir);
        }
        var filesBefore = Directory.GetFiles(uploadDir);

        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Transaction failure rollback test"), "Description");

        var fileContent = new ByteArrayContent(jpegBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "Image", "rollback_target.jpg");

        // Act
        var response = await client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // Verify that no orphan file was left on disk in uploadDir
        var filesAfter = Directory.GetFiles(uploadDir);
        filesAfter.Length.Should().Be(filesBefore.Length, "The uploaded file must be cleaned up on DB rollback.");

        crashConnection.Close();
        crashConnection.Dispose();
    }
}
