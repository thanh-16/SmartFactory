using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SmartFactory.Api.Data;
using SmartFactory.Api.Exceptions;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Repositories;
using SmartFactory.Api.Services;
using SmartFactory.Tests.Fixtures;
using SmartFactory.Tests.Helpers;
using Xunit;

namespace SmartFactory.Tests.Integration;

public class EmpiricalChallengerAdversarialTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public EmpiricalChallengerAdversarialTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Concurrency_10ConcurrentDecisions_ConsistentlyYields_Exactly1Success_And9Conflicts()
    {
        // Execute 3 distinct concurrent rounds on 3 fresh NCRs to verify reproducibility and zero flakiness
        for (int trial = 1; trial <= 3; trial++)
        {
            // Arrange: Create a fresh NCR
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("1"), "LotId");
            form.Add(new StringContent("1"), "StationId");
            form.Add(new StringContent("2"), "ReportedByUserId");
            form.Add(new StringContent("Crack"), "DefectType");
            form.Add(new StringContent("Major"), "Severity");
            form.Add(new StringContent($"Empirical Trial #{trial} Concurrency"), "Description");

            var inspectResponse = await _client.PostAsync("/api/ncr-reports/inspect", form);
            inspectResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var report = await inspectResponse.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
            report.Should().NotBeNull();

            // Act: Dispatch 10 concurrent resolution requests
            const int concurrentCount = 10;
            var tasks = Enumerable.Range(1, concurrentCount).Select(i =>
            {
                var req = new NcrDecisionRequest
                {
                    NcrReportId = report!.Id,
                    Decision = (i % 2 == 0) ? "Rework" : "Scrap",
                    Notes = $"Trial {trial} Attempt {i}",
                    ApprovedByUserId = 1
                };
                return _client.PostAsJsonAsync("/api/ncr-decisions", req);
            }).ToArray();

            var responses = await Task.WhenAll(tasks);

            // Assert: Exactly 1 HTTP 201 Created and exactly 9 HTTP 409 Conflict (Zero 500s)
            var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
            var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
            var serverErrorCount = responses.Count(r => r.StatusCode == HttpStatusCode.InternalServerError);

            createdCount.Should().Be(1, $"Trial {trial}: Exactly one concurrent decision must succeed.");
            conflictCount.Should().Be(9, $"Trial {trial}: Exactly nine concurrent decisions must be rejected with 409 Conflict.");
            serverErrorCount.Should().Be(0, $"Trial {trial}: Zero 500 Internal Server Errors permitted.");

            // Verify ProblemDetails for all 409 Conflict responses
            var conflictResponses = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict);
            foreach (var conflictResp in conflictResponses)
            {
                conflictResp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
                var problem = await conflictResp.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
                problem.Should().NotBeNull();
                problem!.Status.Should().Be((int)HttpStatusCode.Conflict);
                problem.Title.Should().Be("Conflict");
            }

            // Verify Database: Exactly 1 decision record persisted
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
            var countInDb = await db.NcrDecisions.CountAsync(d => d.NcrReportId == report!.Id);
            countInDb.Should().Be(1, $"Trial {trial}: Only 1 decision row persisted in database.");
        }
    }

    [Fact]
    public async Task Concurrency_20ConcurrentDecisions_Yields_Exactly1Success_And19Conflicts()
    {
        // Stress test under higher concurrency (20 simultaneous threads)
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Deformation"), "DefectType");
        form.Add(new StringContent("Critical"), "Severity");
        form.Add(new StringContent("20-Thread Concurrency Stress Test"), "Description");

        var inspectResponse = await _client.PostAsync("/api/ncr-reports/inspect", form);
        inspectResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await inspectResponse.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        report.Should().NotBeNull();

        const int concurrentCount = 20;
        var tasks = Enumerable.Range(1, concurrentCount).Select(i =>
        {
            var req = new NcrDecisionRequest
            {
                NcrReportId = report!.Id,
                Decision = "Rework",
                Notes = $"20-thread attempt {i}",
                ApprovedByUserId = 1
            };
            return _client.PostAsJsonAsync("/api/ncr-decisions", req);
        }).ToArray();

        var responses = await Task.WhenAll(tasks);

        var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        createdCount.Should().Be(1, "Exactly one decision must succeed out of 20 concurrent attempts.");
        conflictCount.Should().Be(19, "All 19 other attempts must yield 409 Conflict.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(100)]
    [InlineData(99999)]
    public async Task Pareto_SingleDefectCategory_Yields100PercentCumulative(int defectCount)
    {
        var mockRepo = Substitute.For<IDashboardRepository>();
        mockRepo.GetDefectCountsByTypeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<(string DefectType, int Count)>
            {
                ("Scratch", defectCount)
            }));

        var sut = new DashboardService(mockRepo);
        var result = await sut.GetParetoAnalysisAsync();

        result.TotalDefects.Should().Be(defectCount);
        result.Items.Should().HaveCount(1);
        result.Items[0].Percentage.Should().Be(100.0);
        result.Items[0].CumulativePercentage.Should().Be(100.0);
    }

    [Fact]
    public async Task Pareto_ZeroDefects_ReturnsEmptyItemsAndZeroTotal()
    {
        var mockRepo = Substitute.For<IDashboardRepository>();
        mockRepo.GetDefectCountsByTypeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<(string DefectType, int Count)>()));

        var sut = new DashboardService(mockRepo);
        var result = await sut.GetParetoAnalysisAsync();

        result.TotalDefects.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Pareto_OddCountsAndPrimes_MaintainsMathematicalInvariants()
    {
        // Prime defect distribution: 17 + 13 + 7 + 3 + 1 = 41 defects
        // 17/41 = 41.4634...% -> 41.46%
        // 13/41 = 31.7073...% -> 31.71%
        // 7/41 = 17.0731...% -> 17.07%
        // 3/41 = 7.3170...% -> 7.32%
        // 1/41 = 2.4390...% -> 2.44%
        var mockRepo = Substitute.For<IDashboardRepository>();
        mockRepo.GetDefectCountsByTypeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<(string DefectType, int Count)>
            {
                ("TypeE", 1),
                ("TypeA", 17),
                ("TypeD", 3),
                ("TypeB", 13),
                ("TypeC", 7)
            }));

        var sut = new DashboardService(mockRepo);
        var result = await sut.GetParetoAnalysisAsync();

        result.TotalDefects.Should().Be(41);
        result.Items.Should().HaveCount(5);

        // Invariant 1: Strictly sorted descending by count
        for (int i = 0; i < result.Items.Count - 1; i++)
        {
            result.Items[i].Count.Should().BeGreaterThanOrEqualTo(result.Items[i + 1].Count);
        }

        // Invariant 2: Monotonically increasing cumulative percentages
        for (int i = 0; i < result.Items.Count - 1; i++)
        {
            result.Items[i + 1].CumulativePercentage.Should().BeGreaterThanOrEqualTo(result.Items[i].CumulativePercentage);
        }

        // Invariant 3: Last item is EXACTLY 100.0% (no floating point truncation/drift)
        result.Items.Last().CumulativePercentage.Should().Be(100.0);

        // Invariant 4: No item exceeds 100.0%
        result.Items.All(item => item.CumulativePercentage <= 100.0).Should().BeTrue();
    }

    [Fact]
    public async Task Pareto_HighCardinalityOddCounts_ReachesExact100Percent()
    {
        // 25 defect types, all with 3 defects = 75 total defects (each 1/25 = 4%)
        var data = Enumerable.Range(1, 25)
            .Select(i => ($"Defect_{i:D2}", 3))
            .ToList();

        var mockRepo = Substitute.For<IDashboardRepository>();
        mockRepo.GetDefectCountsByTypeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(data));

        var sut = new DashboardService(mockRepo);
        var result = await sut.GetParetoAnalysisAsync();

        result.TotalDefects.Should().Be(75);
        result.Items.Should().HaveCount(25);
        result.Items.Last().CumulativePercentage.Should().Be(100.0);
    }

    [Fact]
    public async Task Exact5MbBoundary_UnitLevel_5242880BytesPasses_5242881BytesThrows413()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), "EmpiricalBoundaryTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        try
        {
            var sut = new FileStorageService(tempFolder);

            // 1. Exact 5MB (5,242,880 bytes) must succeed
            var exactBytes = TestFileHelper.CreateExact5MbBytes();
            exactBytes.Length.Should().Be(5_242_880);
            var passFile = TestFileHelper.CreateFormFile(exactBytes, "exact5mb.jpg", "image/jpeg");

            var savedPath = await sut.SaveFileAsync(passFile, "boundary");
            savedPath.Should().StartWith("/uploads/boundary/");
            var diskPath = Path.Combine(tempFolder, savedPath.TrimStart('/'));
            File.Exists(diskPath).Should().BeTrue("Exact 5MB file must be written to disk.");

            // 2. Exact 5MB + 1 byte (5,242,881 bytes) must throw PayloadTooLargeException
            var overBytes = TestFileHelper.Create5MbPlusOneBytes();
            overBytes.Length.Should().Be(5_242_881);
            var failFile = TestFileHelper.CreateFormFile(overBytes, "over5mb.jpg", "image/jpeg");

            var act = () => sut.SaveFileAsync(failFile, "boundary");
            await act.Should().ThrowAsync<PayloadTooLargeException>()
                .WithMessage("*exceeds the maximum allowed limit of 5242880 bytes (5MB)*");
        }
        finally
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Exact5MbBoundary_HttpApiLevel_5242880BytesYields201_5242881BytesYields413()
    {
        // 1. Exact 5MB -> POST /api/ncr-reports/inspect -> HTTP 201 Created
        var exactBytes = TestFileHelper.CreateExact5MbBytes();
        using var passForm = new MultipartFormDataContent();
        passForm.Add(new StringContent("1"), "LotId");
        passForm.Add(new StringContent("1"), "StationId");
        passForm.Add(new StringContent("2"), "ReportedByUserId");
        passForm.Add(new StringContent("Crack"), "DefectType");
        passForm.Add(new StringContent("Minor"), "Severity");
        passForm.Add(new StringContent("Exact 5MB boundary HTTP test"), "Description");

        var passFileContent = new ByteArrayContent(exactBytes);
        passFileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        passForm.Add(passFileContent, "Image", "exact5mb_http.jpg");

        var passResponse = await _client.PostAsync("/api/ncr-reports/inspect", passForm);
        passResponse.StatusCode.Should().Be(HttpStatusCode.Created, "Exact 5,242,880 bytes must return HTTP 201 Created.");

        // 2. Exact 5MB + 1 byte -> POST /api/ncr-reports/inspect -> HTTP 413 Payload Too Large
        var overBytes = TestFileHelper.Create5MbPlusOneBytes();
        using var failForm = new MultipartFormDataContent();
        failForm.Add(new StringContent("1"), "LotId");
        failForm.Add(new StringContent("1"), "StationId");
        failForm.Add(new StringContent("2"), "ReportedByUserId");
        failForm.Add(new StringContent("Deformation"), "DefectType");
        failForm.Add(new StringContent("Major"), "Severity");
        failForm.Add(new StringContent("5MB + 1 byte boundary HTTP test"), "Description");

        var failFileContent = new ByteArrayContent(overBytes);
        failFileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        failForm.Add(failFileContent, "Image", "over5mb_http.jpg");

        var failResponse = await _client.PostAsync("/api/ncr-reports/inspect", failForm);
        failResponse.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, "Exact 5,242,881 bytes must return HTTP 413 Payload Too Large.");
        failResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await failResponse.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(StatusCodes.Status413PayloadTooLarge);
        problem.Title.Should().Be("Payload Too Large");
        problem.Detail.Should().Contain("5242880 bytes (5MB)");
    }
}
