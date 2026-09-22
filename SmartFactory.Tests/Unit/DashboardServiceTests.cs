using FluentAssertions;
using NSubstitute;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Repositories;
using SmartFactory.Api.Services;
using Xunit;

namespace SmartFactory.Tests.Unit;

public class DashboardServiceTests
{
    private readonly IDashboardRepository _dashboardRepository;
    private readonly DashboardService _sut;

    public DashboardServiceTests()
    {
        _dashboardRepository = Substitute.For<IDashboardRepository>();
        _sut = new DashboardService(_dashboardRepository);
    }

    [Fact]
    public async Task GetParetoAnalysisAsync_WhenDefectsExist_ReturnsSortedItemsWithLastCumulativeAtExactly100()
    {
        // Arrange
        var mockData = new List<(string DefectType, int Count)>
        {
            ("Deformation", 15),
            ("Crack", 45),
            ("Scratch", 30),
            ("MissingPart", 10)
        };

        _dashboardRepository.GetDefectCountsByTypeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockData));

        // Act
        var result = await _sut.GetParetoAnalysisAsync();

        // Assert
        result.Should().NotBeNull();
        result.TotalDefects.Should().Be(100);
        result.Items.Should().HaveCount(4);

        // Verify descending sort
        result.Items[0].DefectType.Should().Be("Crack");
        result.Items[0].Count.Should().Be(45);
        result.Items[0].Percentage.Should().Be(45.0);
        result.Items[0].CumulativePercentage.Should().Be(45.0);

        result.Items[1].DefectType.Should().Be("Scratch");
        result.Items[1].Count.Should().Be(30);
        result.Items[1].Percentage.Should().Be(30.0);
        result.Items[1].CumulativePercentage.Should().Be(75.0);

        result.Items[2].DefectType.Should().Be("Deformation");
        result.Items[2].Count.Should().Be(15);
        result.Items[2].Percentage.Should().Be(15.0);
        result.Items[2].CumulativePercentage.Should().Be(90.0);

        // Acceptance criterion: Last category cumulative percentage MUST BE EXACTLY 100.0%
        result.Items[3].DefectType.Should().Be("MissingPart");
        result.Items[3].Count.Should().Be(10);
        result.Items[3].Percentage.Should().Be(10.0);
        result.Items[3].CumulativePercentage.Should().Be(100.0);
    }

    [Fact]
    public async Task GetParetoAnalysisAsync_WhenOddDefectCounts_LastItemStillReachesExactly100()
    {
        // Arrange: 7 defects across 3 categories (3, 2, 2) -> 3/7 = 42.86%, 2/7 = 28.57%
        // Floating point arithmetic without exact handling could result in 99.99% or 100.01%
        var mockData = new List<(string DefectType, int Count)>
        {
            ("Crack", 3),
            ("Scratch", 2),
            ("DimensionError", 2)
        };

        _dashboardRepository.GetDefectCountsByTypeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockData));

        // Act
        var result = await _sut.GetParetoAnalysisAsync();

        // Assert
        result.Items.Should().HaveCount(3);
        result.Items.Last().CumulativePercentage.Should().Be(100.0);
    }

    [Fact]
    public async Task GetParetoAnalysisAsync_WhenNoDefects_ReturnsEmptyItems()
    {
        // Arrange
        _dashboardRepository.GetDefectCountsByTypeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<(string DefectType, int Count)>()));

        // Act
        var result = await _sut.GetParetoAnalysisAsync();

        // Assert
        result.TotalDefects.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetParetoAnalysisAsync_WithSingleDefectCategory_ReturnsSingleItemWith100Percent()
    {
        // Arrange: A single defect category must result in exactly 100.0% cumulative percentage
        var mockData = new List<(string DefectType, int Count)>
        {
            ("Crack", 12)
        };

        _dashboardRepository.GetDefectCountsByTypeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mockData));

        // Act
        var result = await _sut.GetParetoAnalysisAsync();

        // Assert
        result.Should().NotBeNull();
        result.TotalDefects.Should().Be(12);
        result.Items.Should().HaveCount(1);
        result.Items[0].DefectType.Should().Be("Crack");
        result.Items[0].Count.Should().Be(12);
        result.Items[0].Percentage.Should().Be(100.0);
        result.Items[0].CumulativePercentage.Should().Be(100.0);
    }
}
