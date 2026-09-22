using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Models.Entities;
using SmartFactory.Api.Services;
using SmartFactory.Tests.Fixtures;
using Xunit;

namespace SmartFactory.Tests.Integration;

[Collection("SequentialIntegrationTests")]
public class SpcIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SpcIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task TC_SPC_05_GetStationSpcMetrics_ValidStation_ReturnsCorrectMetricsAndPointList()
    {
        // Act: GET /api/dashboard/spc/1
        var response = await _client.GetAsync("/api/dashboard/spc/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var metrics = await response.Content.ReadFromJsonAsync<StationSpcMetricsDto>(_jsonOptions);

        metrics.Should().NotBeNull();
        metrics!.WorkStationId.Should().Be(1);
        metrics.StationCode.Should().Be("ST-01");
        metrics.StationName.Should().NotBeNullOrWhiteSpace();
        metrics.RecentPoints.Should().NotBeEmpty();
        metrics.CurrentUcl.Should().BeGreaterThan(0);
        metrics.HistoricalMeanDefectRate.Should().BeGreaterThan(0);
        metrics.ProcessCapabilityCpk.Should().BeGreaterThan(0);
        metrics.ProcessStatus.Should().BeOneOf("InControl", "Warning", "OutOfControl");
    }

    [Fact]
    public async Task GetStationSpcMetrics_NonExistentStation_Returns404NotFound()
    {
        // Act: GET /api/dashboard/spc/99999
        var response = await _client.GetAsync("/api/dashboard/spc/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)HttpStatusCode.NotFound);
        problem.Title.Should().Be("Work Station Not Found");
    }

    [Fact]
    public async Task GetAllSpcMetrics_ReturnsAllActiveStations()
    {
        // Act: GET /api/dashboard/spc
        var response = await _client.GetAsync("/api/dashboard/spc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<StationSpcMetricsDto>>(_jsonOptions);

        list.Should().NotBeNull();
        list!.Count.Should().BeGreaterOrEqualTo(3);
        list.Should().Contain(s => s.StationCode == "ST-01");
        list.Should().Contain(s => s.StationCode == "ST-02");
        list.Should().Contain(s => s.StationCode == "ST-03");
    }

    [Fact]
    public async Task TC_SPC_06_RunSpcScanAsync_WhenViolationFound_ReturnsPredictiveAlert()
    {
        // Arrange: Insert hourly metrics on Station 2 that violate Nelson Rule 1 (Rate > UCL)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
            var now = DateTime.UtcNow;

            db.StationHourlyMetrics.Add(new StationHourlyMetric
            {
                WorkStationId = 2,
                WindowStartTime = now.AddHours(-2),
                WindowEndTime = now.AddHours(-1),
                TotalInspected = 100,
                TotalDefects = 2,
                DefectRate = 0.02,
                UpperControlLimit = 0.05,
                LowerControlLimit = 0.0,
                DominantDefectType = "Porosity",
                IsWarningTriggered = false
            });

            db.StationHourlyMetrics.Add(new StationHourlyMetric
            {
                WorkStationId = 2,
                WindowStartTime = now.AddHours(-1),
                WindowEndTime = now,
                TotalInspected = 100,
                TotalDefects = 8,
                DefectRate = 0.08, // Exceeds UCL 0.05 -> Nelson Rule 1
                UpperControlLimit = 0.05,
                LowerControlLimit = 0.0,
                DominantDefectType = "Porosity",
                IsWarningTriggered = false
            });

            await db.SaveChangesAsync();
        }

        // Act: Directly execute RunSpcScanAsync
        List<PredictiveAlertPayload> alerts;
        using (var scope = _factory.Services.CreateScope())
        {
            var spcService = scope.ServiceProvider.GetRequiredService<ISpcAnalysisService>();
            alerts = await spcService.RunSpcScanAsync();
        }

        // Assert: Alert triggered for Station 2
        alerts.Should().NotBeNull();
        var alert = alerts.Find(a => a.WorkStationId == 2);
        alert.Should().NotBeNull();
        alert!.AlertLevel.Should().Be("Critical");
        alert.RuleViolated.Should().Be("NelsonRule1");
        alert.CurrentDefectRate.Should().Be(0.08);
        alert.DominantDefectType.Should().Be("Porosity");
        alert.RecommendedAction.Should().NotBeNullOrWhiteSpace();

        // Verify that the Station API now exposes the active alert
        var stationRes = await _client.GetAsync("/api/dashboard/spc/2");
        stationRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var stationDto = await stationRes.Content.ReadFromJsonAsync<StationSpcMetricsDto>(_jsonOptions);
        stationDto.Should().NotBeNull();
        stationDto!.ActiveAlert.Should().NotBeNull();
        stationDto.ActiveAlert!.RuleViolated.Should().Be("NelsonRule1");
    }
}
