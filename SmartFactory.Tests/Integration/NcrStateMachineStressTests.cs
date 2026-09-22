using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Tests.Fixtures;
using SmartFactory.Tests.Helpers;
using Xunit;

namespace SmartFactory.Tests.Integration;

public class NcrStateMachineStressTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public NcrStateMachineStressTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Stress01_CriticalDefect_LocksProductionLot_AndIncrementsDefectQuantity()
    {
        // Arrange
        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Critical"), "Severity");
        form.Add(new StringContent("Empirical stress: critical defect check"), "Description");

        var fileContent = new ByteArrayContent(jpegBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "Image", "critical_stress.jpg");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert HTTP response
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await response.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        report.Should().NotBeNull();
        report!.Severity.Should().Be("Critical");
        report.LotStatus.Should().Be("Locked");

        // Assert Database State
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var lot = await db.ProductionLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == 1);
        lot.Should().NotBeNull();
        lot!.Status.Should().Be("Locked");
        lot.DefectQuantity.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Stress02_MajorDefect_LocksProductionLot()
    {
        // Arrange
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Deformation"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Empirical stress: major defect check"), "Description");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await response.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        report.Should().NotBeNull();
        report!.LotStatus.Should().Be("Locked");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var lot = await db.ProductionLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == 1);
        lot!.Status.Should().Be("Locked");
    }

    [Fact]
    public async Task Stress03_MinorDefect_PreservesProductionLotInProgressStatus()
    {
        // Arrange
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("2"), "LotId");
        form.Add(new StringContent("2"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Scratch"), "DefectType");
        form.Add(new StringContent("Minor"), "Severity");
        form.Add(new StringContent("Empirical stress: minor defect preservation"), "Description");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await response.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        report.Should().NotBeNull();
        report!.LotStatus.Should().Be("InProgress");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var lot = await db.ProductionLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == 2);
        lot!.Status.Should().Be("InProgress");
    }

    [Fact]
    public async Task Stress04_CaseInsensitiveDefectSeverity_LocksLot()
    {
        // Arrange: mixed case "cRiTiCaL"
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Burr"), "DefectType");
        form.Add(new StringContent("cRiTiCaL"), "Severity");
        form.Add(new StringContent("Empirical stress: mixed case severity check"), "Description");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await response.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        report.Should().NotBeNull();
        report!.LotStatus.Should().Be("Locked");
    }

    [Fact]
    public async Task Stress05_ForemanReworkDecision_UnlocksLockedLotToInProgress_AndResolvesNcr()
    {
        // Arrange: Create inspection report with Critical severity to lock lot
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Critical"), "Severity");
        form.Add(new StringContent("Empirical lock before rework"), "Description");

        var inspectResponse = await _client.PostAsync("/api/ncr-reports/inspect", form);
        inspectResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await inspectResponse.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        report.Should().NotBeNull();
        report!.LotStatus.Should().Be("Locked");

        // Act: Foreman issues Rework decision
        var reworkRequest = new NcrDecisionRequest
        {
            NcrReportId = report.Id,
            Decision = "Rework",
            Notes = "Re-work component on Station 1",
            ApprovedByUserId = 1
        };
        var decisionResponse = await _client.PostAsJsonAsync("/api/ncr-decisions", reworkRequest);

        // Assert
        decisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var decision = await decisionResponse.Content.ReadFromJsonAsync<NcrDecisionResponse>(_jsonOptions);
        decision.Should().NotBeNull();
        decision!.Decision.Should().Be("Rework");
        decision.ProductionLotStatus.Should().Be("InProgress");
        decision.NcrReportStatus.Should().Be("Resolved");

        // Verify in Database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var lot = await db.ProductionLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == 1);
        lot!.Status.Should().Be("InProgress");

        var ncr = await db.NcrReports.AsNoTracking().FirstOrDefaultAsync(n => n.Id == report.Id);
        ncr!.Status.Should().Be("Resolved");
    }

    [Fact]
    public async Task Stress06_DuplicateDecision_OnAlreadyResolvedNcr_Returns409Conflict()
    {
        // Arrange: Step 1 - create an NCR report
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Duplicate decision test NCR"), "Description");

        var inspectResponse = await _client.PostAsync("/api/ncr-reports/inspect", form);
        inspectResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await inspectResponse.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        report.Should().NotBeNull();

        // Step 2: First decision (Rework) -> resolves NCR
        var firstDecision = new NcrDecisionRequest
        {
            NcrReportId = report!.Id,
            Decision = "Rework",
            Notes = "Initial valid resolution",
            ApprovedByUserId = 1
        };
        var firstResponse = await _client.PostAsJsonAsync("/api/ncr-decisions", firstDecision);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act: Step 3 - Attempt duplicate decision on already resolved NCR
        var secondDecision = new NcrDecisionRequest
        {
            NcrReportId = report.Id,
            Decision = "Scrap",
            Notes = "Attempted duplicate resolution",
            ApprovedByUserId = 1
        };
        var secondResponse = await _client.PostAsJsonAsync("/api/ncr-decisions", secondDecision);

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        secondResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await secondResponse.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)HttpStatusCode.Conflict);
        problem.Title.Should().Be("Conflict");
        problem.Detail.Should().Contain("has already been resolved");

        // Verify Database integrity: lot status must still be InProgress (from Rework), not overwritten by Scrap
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var lot = await db.ProductionLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == 1);
        lot!.Status.Should().Be("InProgress");

        // Verify decision count: exactly 1 decision recorded
        var decisionCount = await db.NcrDecisions.CountAsync(d => d.NcrReportId == report.Id);
        decisionCount.Should().Be(1);
    }

    [Fact]
    public async Task Stress07_ScrapDecision_SetsProductionLotToScrapped()
    {
        // Arrange
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Burn"), "DefectType");
        form.Add(new StringContent("Critical"), "Severity");
        form.Add(new StringContent("Irreparable burn damage"), "Description");

        var inspectResponse = await _client.PostAsync("/api/ncr-reports/inspect", form);
        inspectResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await inspectResponse.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);

        // Act: Issue Scrap decision
        var scrapRequest = new NcrDecisionRequest
        {
            NcrReportId = report!.Id,
            Decision = "Scrap",
            Notes = "Scrap entire lot due to irreparable burn",
            ApprovedByUserId = 1
        };
        var decisionResponse = await _client.PostAsJsonAsync("/api/ncr-decisions", scrapRequest);

        // Assert
        decisionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var decision = await decisionResponse.Content.ReadFromJsonAsync<NcrDecisionResponse>(_jsonOptions);
        decision!.ProductionLotStatus.Should().Be("Scrapped");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var lot = await db.ProductionLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == 1);
        lot!.Status.Should().Be("Scrapped");
    }

    [Fact]
    public async Task Stress08_NonExistentLot_Returns404NotFoundProblemDetails()
    {
        // Arrange
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("99999"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Minor"), "Severity");
        form.Add(new StringContent("Non-existent lot test"), "Description");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)HttpStatusCode.NotFound);
        problem.Title.Should().Be("Resource Not Found");
        problem.Detail.Should().Contain("Production lot with ID 99999 not found");
    }

    [Fact]
    public async Task Stress09_NonExistentNcr_Decision_Returns404NotFoundProblemDetails()
    {
        // Arrange
        var decisionRequest = new NcrDecisionRequest
        {
            NcrReportId = 99999,
            Decision = "Rework",
            Notes = "Non-existent NCR",
            ApprovedByUserId = 1
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/ncr-decisions", decisionRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)HttpStatusCode.NotFound);
        problem.Title.Should().Be("Resource Not Found");
        problem.Detail.Should().Contain("NCR Report with ID 99999 not found");
    }

    [Fact]
    public async Task Stress10_ConcurrentDecisions_OnSameNcr_AtMostOneSucceeds()
    {
        // Arrange: Create a new NCR report
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Contamination"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Concurrent decision stress test"), "Description");

        var inspectResponse = await _client.PostAsync("/api/ncr-reports/inspect", form);
        inspectResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await inspectResponse.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        report.Should().NotBeNull();

        // Act: Dispatch 10 concurrent requests to resolve the exact same NCR
        const int concurrentRequests = 10;
        var tasks = Enumerable.Range(1, concurrentRequests).Select(i =>
        {
            var req = new NcrDecisionRequest
            {
                NcrReportId = report!.Id,
                Decision = (i % 2 == 0) ? "Rework" : "Scrap",
                Notes = $"Concurrent decision attempt #{i}",
                ApprovedByUserId = 1
            };
            return _client.PostAsJsonAsync("/api/ncr-decisions", req);
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        // Assert: At least one succeeded with 201 Created
        var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictOrLockCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.InternalServerError);

        // Concurrency Oracle: Exactly 1 request succeeds in creating decision; all others are rejected (409 or lock error)
        createdCount.Should().Be(1, "Exactly one concurrent decision can successfully be created.");
        conflictOrLockCount.Should().Be(concurrentRequests - 1, "All other concurrent attempts must be rejected.");

        // Verify in Database: Strictly single decision record persisted (No double-resolution race condition)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var decisionsInDb = await db.NcrDecisions.Where(d => d.NcrReportId == report!.Id).ToListAsync();
        decisionsInDb.Should().HaveCount(1, "Only a single decision record must exist in DB.");

        var ncrInDb = await db.NcrReports.AsNoTracking().FirstOrDefaultAsync(n => n.Id == report!.Id);
        ncrInDb!.Status.Should().Be("Resolved");
    }
}
