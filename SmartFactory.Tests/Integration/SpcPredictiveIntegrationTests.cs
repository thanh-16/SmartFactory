using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Models.Entities;
using SmartFactory.Api.Services;
using SmartFactory.Tests.Fixtures;
using Xunit;

namespace SmartFactory.Tests.Integration;

/// <summary>
/// Integration tests for Sprint 4: AI Predictive Quality & Statistical Process Control (SPC).
/// Tests endpoints GET /api/dashboard/spc, GET /api/dashboard/spc/{stationId},
/// and ISpcAnalysisService drift detection algorithms against SQLite in-memory database.
/// </summary>
[Collection("SequentialIntegrationTests")]
public class SpcPredictiveIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public SpcPredictiveIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ==========================================
    // 1. GET ALL STATIONS SPC METRICS
    // ==========================================

    [Fact]
    public async Task GetAllSpcMetrics_Returns200OkWithAllActiveStations()
    {
        // Act
        var response = await _client.GetAsync("/api/dashboard/spc");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<List<StationSpcMetricsDto>>(_jsonOptions);
        list.Should().NotBeNull();
        list!.Count.Should().BeGreaterThanOrEqualTo(3, "Phải bao gồm tối thiểu 3 trạm máy được seed sẵn (ST-01, ST-02, ST-03)");

        foreach (var station in list)
        {
            station.WorkStationId.Should().BeGreaterThan(0);
            station.StationCode.Should().NotBeNullOrWhiteSpace();
            station.StationName.Should().NotBeNullOrWhiteSpace();
            station.HistoricalMeanDefectRate.Should().BeGreaterThanOrEqualTo(0.0);
            station.CurrentUcl.Should().BeGreaterThan(0.0);
            station.CurrentLcl.Should().BeGreaterThanOrEqualTo(0.0);
            station.ProcessCapabilityCpk.Should().BeGreaterThanOrEqualTo(0.0);
            station.ProcessStatus.Should().BeOneOf("InControl", "Warning", "OutOfControl");
            station.RecentPoints.Should().NotBeNull();
        }
    }

    // ==========================================
    // 2. GET SINGLE STATION SPC METRICS
    // ==========================================

    [Fact]
    public async Task GetStationSpc_WhenStationExists_Returns200OkWithMetricsAndPoints()
    {
        // Arrange: WorkStation ID 1 (ST-01) is seeded in FactoryDbContext
        int stationId = 1;

        // Act
        var response = await _client.GetAsync($"/api/dashboard/spc/{stationId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var metrics = await response.Content.ReadFromJsonAsync<StationSpcMetricsDto>(_jsonOptions);
        metrics.Should().NotBeNull();
        metrics!.WorkStationId.Should().Be(stationId);
        metrics.StationCode.Should().Be("ST-01");
        metrics.StationName.Should().Be("Trạm Cắt & Dập");
        metrics.ProcessCapabilityCpk.Should().BeGreaterThan(0.0);
        metrics.RecentPoints.Should().NotBeEmpty();

        foreach (var point in metrics.RecentPoints)
        {
            point.Value.Should().BeGreaterThanOrEqualTo(0.0);
            point.CenterLine.Should().BeGreaterThanOrEqualTo(0.0);
            point.UCL.Should().BeGreaterThan(0.0);
            point.LCL.Should().BeGreaterThanOrEqualTo(0.0);
        }
    }

    [Fact]
    public async Task GetStationSpc_WhenStationDoesNotExist_Returns404NotFoundWithProblemDetails()
    {
        // Arrange: Non-existent ID
        int nonExistentStationId = 99999;

        // Act
        var response = await _client.GetAsync($"/api/dashboard/spc/{nonExistentStationId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(404);
        problem.Title.Should().Contain("Not Found");
        problem.Detail.Should().Contain(nonExistentStationId.ToString());
    }

    // ==========================================
    // 3. DRIFT DETECTION & RULE 1 VIOLATION
    // ==========================================

    [Fact]
    public async Task RunSpcScanAsync_WhenNelsonRule1Violated_FlagsMetricAndReturnsCriticalAlert()
    {
        // Arrange: Create a dedicated workstation and seed metrics exceeding UCL (Rule 1)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var spcService = scope.ServiceProvider.GetRequiredService<ISpcAnalysisService>();

        var testStation = new WorkStation
        {
            Code = $"TEST-R1-{Guid.NewGuid():N}"[..10],
            Name = "Trạm Test Nelson Rule 1",
            Description = "Kiểm thử vi phạm vượt UCL",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.WorkStations.Add(testStation);
        await db.SaveChangesAsync();

        var baseTime = DateTime.UtcNow.AddHours(-3);
        var metrics = new List<StationHourlyMetric>
        {
            new()
            {
                WorkStationId = testStation.Id,
                WindowStartTime = baseTime,
                WindowEndTime = baseTime.AddHours(1),
                TotalInspected = 500,
                TotalDefects = 5,
                DefectRate = 0.010,
                UpperControlLimit = 0.050,
                LowerControlLimit = 0.0,
                DominantDefectType = "Crack"
            },
            new()
            {
                WorkStationId = testStation.Id,
                WindowStartTime = baseTime.AddHours(1),
                WindowEndTime = baseTime.AddHours(2),
                TotalInspected = 500,
                TotalDefects = 10,
                DefectRate = 0.020,
                UpperControlLimit = 0.050,
                LowerControlLimit = 0.0,
                DominantDefectType = "Crack"
            },
            new()
            {
                WorkStationId = testStation.Id,
                WindowStartTime = baseTime.AddHours(2),
                WindowEndTime = baseTime.AddHours(3),
                TotalInspected = 500,
                TotalDefects = 45,
                DefectRate = 0.090, // Exceeds UCL of 0.050 -> Rule 1
                UpperControlLimit = 0.050,
                LowerControlLimit = 0.0,
                DominantDefectType = "Crack"
            }
        };
        db.StationHourlyMetrics.AddRange(metrics);
        await db.SaveChangesAsync();

        // Act: Run SPC scan
        var alerts = await spcService.RunSpcScanAsync();

        // Assert 1: Service returns Critical Alert
        var stationAlert = alerts.FirstOrDefault(a => a.WorkStationId == testStation.Id);
        stationAlert.Should().NotBeNull("Hệ thống phải phát hiện và cảnh báo trạm vi phạm Rule 1");
        stationAlert!.RuleViolated.Should().Be("NelsonRule1");
        stationAlert.AlertLevel.Should().Be("Critical");
        stationAlert.AnomalyScore.Should().Be(95);
        stationAlert.CurrentDefectRate.Should().Be(0.090);

        // Assert 2: Database metric record is updated with warning flags
        var latestDbMetric = await db.StationHourlyMetrics
            .Where(m => m.WorkStationId == testStation.Id)
            .OrderByDescending(m => m.WindowStartTime)
            .FirstOrDefaultAsync();

        latestDbMetric.Should().NotBeNull();
        latestDbMetric!.IsWarningTriggered.Should().BeTrue();
        latestDbMetric.TriggeredRule.Should().Be("NelsonRule1");
        latestDbMetric.AnomalyScore.Should().Be(95);

        // Assert 3: API reflects OutOfControl process status and ActiveAlert
        var apiResponse = await _client.GetAsync($"/api/dashboard/spc/{testStation.Id}");
        apiResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var stationSpc = await apiResponse.Content.ReadFromJsonAsync<StationSpcMetricsDto>(_jsonOptions);
        stationSpc.Should().NotBeNull();
        stationSpc!.ProcessStatus.Should().Be("OutOfControl");
        stationSpc.ActiveAlert.Should().NotBeNull();
        stationSpc.ActiveAlert!.RuleViolated.Should().Be("NelsonRule1");
    }

    // ==========================================
    // 4. DRIFT DETECTION & RULE 2 VIOLATION
    // ==========================================

    [Fact]
    public async Task RunSpcScanAsync_WhenNelsonRule2Violated_FlagsMetricAndReturnsWarningAlert()
    {
        // Arrange: 3 consecutive increases where latest > historical mean
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var spcService = scope.ServiceProvider.GetRequiredService<ISpcAnalysisService>();

        var testStation = new WorkStation
        {
            Code = $"TEST-R2-{Guid.NewGuid():N}"[..10],
            Name = "Trạm Test Nelson Rule 2",
            Description = "Kiểm thử xu hướng 3 điểm tăng liên tục",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.WorkStations.Add(testStation);
        await db.SaveChangesAsync();

        var baseTime = DateTime.UtcNow.AddHours(-3);
        var metrics = new List<StationHourlyMetric>
        {
            new()
            {
                WorkStationId = testStation.Id,
                WindowStartTime = baseTime,
                WindowEndTime = baseTime.AddHours(1),
                TotalInspected = 500,
                TotalDefects = 5,
                DefectRate = 0.010,
                UpperControlLimit = 0.060,
                LowerControlLimit = 0.0,
                DominantDefectType = "Scratch"
            },
            new()
            {
                WorkStationId = testStation.Id,
                WindowStartTime = baseTime.AddHours(1),
                WindowEndTime = baseTime.AddHours(2),
                TotalInspected = 500,
                TotalDefects = 10,
                DefectRate = 0.020,
                UpperControlLimit = 0.060,
                LowerControlLimit = 0.0,
                DominantDefectType = "Scratch"
            },
            new()
            {
                WorkStationId = testStation.Id,
                WindowStartTime = baseTime.AddHours(2),
                WindowEndTime = baseTime.AddHours(3),
                TotalInspected = 500,
                TotalDefects = 18,
                DefectRate = 0.036, // 0.036 > 0.020 > 0.010 and 0.036 > historical mean (~0.022) -> Rule 2
                UpperControlLimit = 0.060,
                LowerControlLimit = 0.0,
                DominantDefectType = "Scratch"
            }
        };
        db.StationHourlyMetrics.AddRange(metrics);
        await db.SaveChangesAsync();

        // Act
        var alerts = await spcService.RunSpcScanAsync();

        // Assert 1: Rule 2 Warning Alert generated
        var stationAlert = alerts.FirstOrDefault(a => a.WorkStationId == testStation.Id);
        stationAlert.Should().NotBeNull();
        stationAlert!.RuleViolated.Should().Be("NelsonRule2");
        stationAlert.AlertLevel.Should().Be("Warning");
        stationAlert.AnomalyScore.Should().Be(75);
        stationAlert.RootCauseHypothesis.Should().Contain("Scratch");

        // Assert 2: API returns Warning status
        var apiResponse = await _client.GetAsync($"/api/dashboard/spc/{testStation.Id}");
        apiResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var stationSpc = await apiResponse.Content.ReadFromJsonAsync<StationSpcMetricsDto>(_jsonOptions);
        stationSpc.Should().NotBeNull();
        stationSpc!.ProcessStatus.Should().Be("Warning");
        stationSpc.ActiveAlert.Should().NotBeNull();
        stationSpc.ActiveAlert!.RuleViolated.Should().Be("NelsonRule2");
    }

    // ==========================================
    // 5. DRIFT DETECTION & RULE 3 VIOLATION
    // ==========================================

    [Fact]
    public async Task RunSpcScanAsync_WhenNelsonRule3Violated_FlagsMetricAndReturnsShiftWarningAlert()
    {
        // Arrange: 8 consecutive points above historical mean (e.g. baseline mean 0.015, points 0.022-0.026)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var spcService = scope.ServiceProvider.GetRequiredService<ISpcAnalysisService>();

        var testStation = new WorkStation
        {
            Code = $"TEST-R3-{Guid.NewGuid():N}"[..10],
            Name = "Trạm Test Nelson Rule 3",
            Description = "Kiểm thử hiện tượng trôi trục quá trình (Process Shift)",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.WorkStations.Add(testStation);
        await db.SaveChangesAsync();

        // Seed 1 low point at start to set a lower historical mean, followed by 8 points above mean
        var baseTime = DateTime.UtcNow.AddHours(-10);
        var metrics = new List<StationHourlyMetric>
        {
            new()
            {
                WorkStationId = testStation.Id,
                WindowStartTime = baseTime,
                WindowEndTime = baseTime.AddHours(1),
                TotalInspected = 500,
                TotalDefects = 1,
                DefectRate = 0.002, // Low anchor
                UpperControlLimit = 0.060,
                LowerControlLimit = 0.0,
                DominantDefectType = "Deformation"
            }
        };

        // 8 points fluctuating non-monotonically between 0.024 and 0.028 (historical mean will be ~0.023)
        // Ensure tail is not strictly increasing so Rule 2 is not triggered before Rule 3
        var rates = new double[] { 0.026, 0.024, 0.027, 0.025, 0.028, 0.027, 0.026, 0.025 };
        for (int i = 0; i < rates.Length; i++)
        {
            metrics.Add(new StationHourlyMetric
            {
                WorkStationId = testStation.Id,
                WindowStartTime = baseTime.AddHours(i + 1),
                WindowEndTime = baseTime.AddHours(i + 2),
                TotalInspected = 500,
                TotalDefects = (int)(rates[i] * 500),
                DefectRate = rates[i],
                UpperControlLimit = 0.060,
                LowerControlLimit = 0.0,
                DominantDefectType = "Deformation"
            });
        }

        db.StationHourlyMetrics.AddRange(metrics);
        await db.SaveChangesAsync();

        // Act
        var alerts = await spcService.RunSpcScanAsync();

        // Assert: Violated Rule 3
        var stationAlert = alerts.FirstOrDefault(a => a.WorkStationId == testStation.Id);
        stationAlert.Should().NotBeNull("8 điểm liên tiếp trên mean phải kích hoạt Nelson Rule 3");
        stationAlert!.RuleViolated.Should().Be("NelsonRule3");
        stationAlert.AlertLevel.Should().Be("Warning");
        stationAlert.AnomalyScore.Should().Be(80);
    }

    // ==========================================
    // 6. BASELINE FALLBACK WHEN NO HOURLY METRICS EXIST
    // ==========================================

    [Fact]
    public async Task GetStationSpc_WhenNoHourlyMetricsExist_ComputesBaselineFromLotsAndNcrs()
    {
        // Arrange: Create new active station without any StationHourlyMetric
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();

        var freshStation = new WorkStation
        {
            Code = $"TEST-NEW-{Guid.NewGuid():N}"[..10],
            Name = "Trạm Mới Chưa Có Dữ Liệu Giờ",
            Description = "Kiểm tra cơ chế tính baseline từ ProductionLots & NCRs",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.WorkStations.Add(freshStation);

        var testLot = new ProductionLot
        {
            LotNumber = $"LOT-SPC-{Guid.NewGuid():N}"[..12],
            ProductName = "Khung Xe Điện EV",
            Quantity = 1000,
            DefectQuantity = 15,
            Status = "InProgress",
            WorkStation = freshStation,
            CreatedAt = DateTime.UtcNow
        };
        db.ProductionLots.Add(testLot);
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/dashboard/spc/{freshStation.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<StationSpcMetricsDto>(_jsonOptions);
        result.Should().NotBeNull();
        result!.WorkStationId.Should().Be(freshStation.Id);
        result.ProcessStatus.Should().Be("InControl");
        result.RecentPoints.Should().HaveCount(6, "Fallback baseline sinh 6 điểm đại diện cho đồ thị");
        result.ProcessCapabilityCpk.Should().BeGreaterThan(0.0);
        result.ActiveAlert.Should().BeNull();
    }
}
