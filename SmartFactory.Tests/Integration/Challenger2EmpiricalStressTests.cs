using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Tests.Fixtures;
using SmartFactory.Tests.Helpers;
using Xunit;

namespace SmartFactory.Tests.Integration;

public class Challenger2EmpiricalStressTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Challenger2EmpiricalStressTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Concurrency_10ConcurrentRequests_ToResolveSameNcr_Exactly1Succeeds_And9Return409Conflict()
    {
        // 1. Arrange: Create a fresh NCR report to resolve
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Concurrency stress target NCR"), "Description");

        var inspectResponse = await _client.PostAsync("/api/ncr-reports/inspect", form);
        inspectResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await inspectResponse.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        report.Should().NotBeNull();
        var ncrId = report!.Id;

        // 2. Act: Fire 10 simultaneous POST requests to resolve this same NCR
        const int concurrentWorkers = 10;
        var tasks = Enumerable.Range(1, concurrentWorkers).Select(i =>
        {
            var request = new NcrDecisionRequest
            {
                NcrReportId = ncrId,
                Decision = (i % 2 == 0) ? "Rework" : "Scrap",
                Notes = $"Concurrent resolution attempt #{i}",
                ApprovedByUserId = 1 // Supervisor
            };
            return _client.PostAsJsonAsync("/api/ncr-decisions", request);
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        // 3. Empirical Concurrency Oracle Assertions:
        // Exactly 1 request succeeds with HTTP 201 Created
        var createdResponses = responses.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
        createdResponses.Should().HaveCount(1, "Exactly one concurrent request must win the resolution race and return 201 Created.");

        var winnerBody = await createdResponses[0].Content.ReadFromJsonAsync<NcrDecisionResponse>(_jsonOptions);
        winnerBody.Should().NotBeNull();
        winnerBody!.NcrReportStatus.Should().Be("Resolved");

        // The remaining 9 requests MUST return HTTP 409 Conflict (Zero 500s or other status codes)
        var conflictResponses = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();
        conflictResponses.Should().HaveCount(9, "The remaining 9 concurrent requests must return HTTP 409 Conflict.");

        var otherStatusResponses = responses.Where(r => r.StatusCode != HttpStatusCode.Created && r.StatusCode != HttpStatusCode.Conflict).ToList();
        otherStatusResponses.Should().BeEmpty("No requests should result in 500 Internal Server Error or other non-409 errors.");

        // Verify RFC 7807 ProblemDetails compliance on all 9 rejected requests
        foreach (var conflictResponse in conflictResponses)
        {
            conflictResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
            var problem = await conflictResponse.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
            problem.Should().NotBeNull();
            problem!.Status.Should().Be((int)HttpStatusCode.Conflict);
            problem.Title.Should().Be("Conflict");
            problem.Detail.Should().Contain("has already been resolved");
        }

        // Database Verification: Exactly 1 decision record persisted for this NCR
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var persistedDecisions = await db.NcrDecisions.Where(d => d.NcrReportId == ncrId).ToListAsync();
        persistedDecisions.Should().HaveCount(1, "Only a single decision record must be persisted in the database.");
    }

    [Fact]
    public async Task LotLocking_StateTransitions_AlreadyLockedLot_ReceivingMinorDefect_PreservesLockedStatus_AndIncrementsDefectQuantity()
    {
        // 1. Initial State: Check Lot 2 baseline
        using var scope1 = _factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var lotInitial = await db1.ProductionLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == 2);
        lotInitial.Should().NotBeNull();
        var baselineDefects = lotInitial!.DefectQuantity;

        // 2. Step 1: Submit a Major defect to lock the lot
        using var formMajor = new MultipartFormDataContent();
        formMajor.Add(new StringContent("2"), "LotId");
        formMajor.Add(new StringContent("2"), "StationId");
        formMajor.Add(new StringContent("2"), "ReportedByUserId");
        formMajor.Add(new StringContent("Crack"), "DefectType");
        formMajor.Add(new StringContent("Major"), "Severity");
        formMajor.Add(new StringContent("Locking lot 2 via Major defect"), "Description");

        var responseMajor = await _client.PostAsync("/api/ncr-reports/inspect", formMajor);
        responseMajor.StatusCode.Should().Be(HttpStatusCode.Created);
        var reportMajor = await responseMajor.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        reportMajor!.LotStatus.Should().Be("Locked");

        // Verify lot is now Locked and DefectQuantity incremented by 1
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var lotLocked = await db2.ProductionLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == 2);
        lotLocked!.Status.Should().Be("Locked");
        lotLocked.DefectQuantity.Should().Be(baselineDefects + 1);

        // 3. Step 2: Submit a Minor defect while the lot is ALREADY Locked
        using var formMinor = new MultipartFormDataContent();
        formMinor.Add(new StringContent("2"), "LotId");
        formMinor.Add(new StringContent("2"), "StationId");
        formMinor.Add(new StringContent("2"), "ReportedByUserId");
        formMinor.Add(new StringContent("Scratch"), "DefectType");
        formMinor.Add(new StringContent("Minor"), "Severity");
        formMinor.Add(new StringContent("Subsequent minor defect on already locked lot"), "Description");

        var responseMinor = await _client.PostAsync("/api/ncr-reports/inspect", formMinor);
        responseMinor.StatusCode.Should().Be(HttpStatusCode.Created);
        var reportMinor = await responseMinor.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        reportMinor!.LotStatus.Should().Be("Locked", "Response must reflect Locked status.");

        // Assert: ProductionLot must KEEP 'Locked' status (NEVER revert to InProgress) and increment DefectQuantity
        using var scope3 = _factory.Services.CreateScope();
        var db3 = scope3.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var lotAfterMinor = await db3.ProductionLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == 2);
        lotAfterMinor!.Status.Should().Be("Locked", "An already locked lot must remain Locked upon receiving a minor defect.");
        lotAfterMinor.DefectQuantity.Should().Be(baselineDefects + 2, "DefectQuantity must increment upon receiving minor defect.");
    }

    [Theory]
    [InlineData("LotId", "99999", "StationId", "1", "ReportedByUserId", "2", "Production lot with ID 99999 not found")]
    [InlineData("LotId", "1", "StationId", "99999", "ReportedByUserId", "2", "Work station with ID 99999 not found")]
    [InlineData("LotId", "1", "StationId", "1", "ReportedByUserId", "99999", "User with ID 99999 not found")]
    public async Task MissingRelations_InspectEndpoint_Returns404ProblemDetails(
        string field1, string val1, 
        string field2, string val2, 
        string field3, string val3, 
        string expectedErrorSnippet)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(val1), field1);
        form.Add(new StringContent(val2), field2);
        form.Add(new StringContent(val3), field3);
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Minor"), "Severity");
        form.Add(new StringContent("Missing relation test"), "Description");

        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)HttpStatusCode.NotFound);
        problem.Title.Should().Be("Resource Not Found");
        problem.Detail.Should().Contain(expectedErrorSnippet);
    }

    [Fact]
    public async Task MissingRelations_DecisionEndpoint_NonExistentNcr_Returns404ProblemDetails()
    {
        var request = new NcrDecisionRequest
        {
            NcrReportId = 99999,
            Decision = "Rework",
            Notes = "Testing non-existent NCR",
            ApprovedByUserId = 1
        };

        var response = await _client.PostAsJsonAsync("/api/ncr-decisions", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)HttpStatusCode.NotFound);
        problem.Title.Should().Be("Resource Not Found");
        problem.Detail.Should().Contain("NCR Report with ID 99999 not found");
    }

    [Fact]
    public async Task MissingRelations_DecisionEndpoint_NonExistentApprover_Returns404ProblemDetails()
    {
        // 1. Arrange: Create a valid NCR first so NCR exists
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Valid NCR for non-existent approver test"), "Description");

        var inspectResponse = await _client.PostAsync("/api/ncr-reports/inspect", form);
        inspectResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await inspectResponse.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        report.Should().NotBeNull();

        // 2. Act: Attempt decision with non-existent approver user (99999)
        var request = new NcrDecisionRequest
        {
            NcrReportId = report!.Id,
            Decision = "Rework",
            Notes = "Testing non-existent approver",
            ApprovedByUserId = 99999
        };

        var response = await _client.PostAsJsonAsync("/api/ncr-decisions", request);

        // 3. Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)HttpStatusCode.NotFound);
        problem.Title.Should().Be("Resource Not Found");
        problem.Detail.Should().Contain("User with ID 99999 not found");
    }

    [Fact]
    public async Task ParetoAnalysis_CalculationRules_SingleCategory100_ZeroDefects_AndDescendingOrder()
    {
        // 1. Zero Defects Test (tested via controller when database has or doesn't have defects)
        // Check GET /api/dashboard/pareto returns 200 OK
        var paretoResponse = await _client.GetAsync("/api/dashboard/pareto");
        paretoResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var paretoData = await paretoResponse.Content.ReadFromJsonAsync<ParetoResponse>(_jsonOptions);
        paretoData.Should().NotBeNull();

        if (paretoData!.TotalDefects > 0)
        {
            // Verify descending order
            for (int i = 0; i < paretoData.Items.Count - 1; i++)
            {
                paretoData.Items[i].Count.Should().BeGreaterThanOrEqualTo(paretoData.Items[i + 1].Count);
            }

            // Verify cumulative percentage ends at exactly 100.0%
            paretoData.Items.Last().CumulativePercentage.Should().Be(100.0);
        }

        // 2. Algorithmic unit check for Single Category = 100.0%
        var singleCategoryList = new List<(string DefectType, int Count)> { ("Crack", 42) };
        var repoMock = NSubstitute.Substitute.For<SmartFactory.Api.Repositories.IDashboardRepository>();
        repoMock.GetDefectCountsByTypeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(singleCategoryList));

        var dashboardService = new SmartFactory.Api.Services.DashboardService(repoMock);
        var singleResult = await dashboardService.GetParetoAnalysisAsync();

        singleResult.TotalDefects.Should().Be(42);
        singleResult.Items.Should().HaveCount(1);
        singleResult.Items[0].Percentage.Should().Be(100.0);
        singleResult.Items[0].CumulativePercentage.Should().Be(100.0);

        // 3. Algorithmic unit check for Zero Defects
        repoMock.GetDefectCountsByTypeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<(string DefectType, int Count)>()));

        var zeroResult = await dashboardService.GetParetoAnalysisAsync();
        zeroResult.TotalDefects.Should().Be(0);
        zeroResult.Items.Should().NotBeNull();
        zeroResult.Items.Should().BeEmpty();
    }
}
