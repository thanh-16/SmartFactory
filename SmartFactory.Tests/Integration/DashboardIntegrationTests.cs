using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Tests.Fixtures;
using Xunit;

namespace SmartFactory.Tests.Integration;

public class DashboardIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DashboardIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSummary_Returns200OkWithLotAndNcrCounts()
    {
        // Act
        var response = await _client.GetAsync("/api/dashboard/summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await response.Content.ReadFromJsonAsync<DashboardSummaryResponse>();
        summary.Should().NotBeNull();
        summary!.TotalLots.Should().BeGreaterThanOrEqualTo(3);
        summary.CompletedLots.Should().BeGreaterThanOrEqualTo(1); // LOT-2026-003 is Completed
    }

    [Fact]
    public async Task GetPareto_Returns200Ok()
    {
        // Arrange: ensure defects exist to verify descending order and cumulative percentage
        using var form1 = new MultipartFormDataContent();
        form1.Add(new StringContent("1"), "LotId");
        form1.Add(new StringContent("1"), "StationId");
        form1.Add(new StringContent("2"), "ReportedByUserId");
        form1.Add(new StringContent("Crack"), "DefectType");
        form1.Add(new StringContent("Minor"), "Severity");
        form1.Add(new StringContent("Pareto Crack 1"), "Description");
        await _client.PostAsync("/api/ncr-reports/inspect", form1);

        using var form2 = new MultipartFormDataContent();
        form2.Add(new StringContent("1"), "LotId");
        form2.Add(new StringContent("1"), "StationId");
        form2.Add(new StringContent("2"), "ReportedByUserId");
        form2.Add(new StringContent("Crack"), "DefectType");
        form2.Add(new StringContent("Minor"), "Severity");
        form2.Add(new StringContent("Pareto Crack 2"), "Description");
        await _client.PostAsync("/api/ncr-reports/inspect", form2);

        using var form3 = new MultipartFormDataContent();
        form3.Add(new StringContent("1"), "LotId");
        form3.Add(new StringContent("1"), "StationId");
        form3.Add(new StringContent("2"), "ReportedByUserId");
        form3.Add(new StringContent("Scratch"), "DefectType");
        form3.Add(new StringContent("Minor"), "Severity");
        form3.Add(new StringContent("Pareto Scratch 1"), "Description");
        await _client.PostAsync("/api/ncr-reports/inspect", form3);

        // Act
        var response = await _client.GetAsync("/api/dashboard/pareto");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var pareto = await response.Content.ReadFromJsonAsync<ParetoResponse>();
        pareto.Should().NotBeNull();
        pareto!.Items.Should().NotBeEmpty();
        pareto.Items.Should().BeInDescendingOrder(x => x.Count);
        pareto.Items.Last().CumulativePercentage.Should().Be(100.0);
    }
}
