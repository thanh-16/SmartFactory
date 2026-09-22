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
        // Act
        var response = await _client.GetAsync("/api/dashboard/pareto");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var pareto = await response.Content.ReadFromJsonAsync<ParetoResponse>();
        pareto.Should().NotBeNull();
    }
}
