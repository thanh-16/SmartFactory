using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Services;
using Xunit;

namespace SmartFactory.Tests.Unit;

public class AiRootCauseAnalysisTests
{
    private readonly AiInspectionService _aiService;

    public AiRootCauseAnalysisTests()
    {
        var configuration = new ConfigurationBuilder().Build();
        _aiService = new AiInspectionService(configuration, NullLogger<AiInspectionService>.Instance);
    }

    [Theory]
    [InlineData("Crack", "Nứt chân đế phôi kim loại")]
    [InlineData("Scratch", "Trầy xước bề mặt chi tiết")]
    [InlineData("Deformation", "Biến dạng cong vênh thanh định hình")]
    [InlineData("Porosity", "Rỗ khí vũng hàn")]
    [InlineData("Contamination", "Dính dầu mỡ và bụi bẩn trước sơn")]
    [InlineData("Burr", "Bavia sắc nhọn tại mép cắt")]
    [InlineData("Other", "Sai lệch gia công bất thường")]
    public void GenerateHeuristicRootCauseAnalysis_ForAll7DefectTypes_ProducesComplete5WhyAnd6M(string defectType, string description)
    {
        // Act
        var result = AiInspectionService.GenerateHeuristicRootCauseAnalysis(defectType, description);

        // Assert 1: Five Whys
        result.Should().NotBeNull();
        result.FiveWhys.Should().HaveCount(5, "Chuỗi 5-Why bắt buộc phải có đúng 5 tầng câu hỏi đào sâu.");
        for (int i = 0; i < 5; i++)
        {
            var item = result.FiveWhys[i];
            item.Step.Should().Be(i + 1);
            item.Question.Should().NotBeNullOrWhiteSpace("Câu hỏi của Why #{0} không được rỗng", i + 1);
            item.Answer.Should().NotBeNullOrWhiteSpace("Câu trả lời của Why #{0} không được rỗng", i + 1);
        }

        // Assert 2: Ishikawa 6M Categories
        result.IshikawaCategories.Should().NotBeNull();
        result.IshikawaCategories.Should().ContainKey(IshikawaCategoryNames.Man);
        result.IshikawaCategories.Should().ContainKey(IshikawaCategoryNames.Machine);
        result.IshikawaCategories.Should().ContainKey(IshikawaCategoryNames.Material);
        result.IshikawaCategories.Should().ContainKey(IshikawaCategoryNames.Method);
        result.IshikawaCategories.Should().ContainKey(IshikawaCategoryNames.Measurement);
        result.IshikawaCategories.Should().ContainKey(IshikawaCategoryNames.Environment);

        foreach (var category in result.IshikawaCategories)
        {
            category.Value.Should().NotBeNull($"Danh mục 6M '{category.Key}' không được null");
            category.Value.Should().NotBeEmpty($"Danh mục 6M '{category.Key}' phải có ít nhất 1 nguyên nhân tiềm ẩn");
            category.Value.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item));
        }

        // Assert 3: Primary Root Cause & CAPA
        result.PrimaryRootCause.Should().NotBeNullOrWhiteSpace("Nguyên nhân cốt lõi không được để trống");
        result.RecommendedCorrectiveAction.Should().NotBeNullOrWhiteSpace("Hành động khắc phục trước mắt phải có dữ liệu");
        result.RecommendedPreventiveAction.Should().NotBeNullOrWhiteSpace("Hành động phòng ngừa lâu dài phải có dữ liệu");
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.85f);
        result.EngineProvider.Should().Be("Deterministic-Heuristic-6M");
    }

    [Fact]
    public async Task InvestigateRootCauseAsync_WhenApiKeyMissing_GracefullyFallsBackToHeuristicWithoutCrash()
    {
        // Act: Gọi service khi không có API Key
        var result = await _aiService.InvestigateRootCauseAsync("Crack", "Critical", "Nứt góc chấn dập", null, null, null);

        // Assert: Không bao giờ crash, trả về Heuristic RCA hoàn chỉnh
        result.Should().NotBeNull();
        result.FiveWhys.Should().HaveCount(5);
        result.IshikawaCategories.Should().HaveCount(6);
        result.PrimaryRootCause.Should().Contain("Poka-Yoke");
    }

    [Theory]
    [InlineData("vết nứt góc chấn phôi thép", "Crack")]
    [InlineData("xước xát bề mặt lớp sơn phủ", "Scratch")]
    [InlineData("cong vênh mép dập vượt quá dung sai", "Deformation")]
    [InlineData("rỗ khí li ti trong vũng hàn", "Porosity")]
    [InlineData("dính dầu bẩn và cặn bụi", "Contamination")]
    [InlineData("bavia gờ kim loại nhọn", "Burr")]
    public void GenerateHeuristicRootCauseAnalysis_WithVietnameseKeywords_InfersCorrectDefectCategory(string description, string expectedCategory)
    {
        // Act
        var result = AiInspectionService.GenerateHeuristicRootCauseAnalysis(string.Empty, description);

        // Assert
        result.Should().NotBeNull();
        result.FiveWhys.Should().HaveCount(5);
        if (expectedCategory == "Crack")
        {
            result.PrimaryRootCause.Should().Contain("Poka-Yoke");
        }
        else if (expectedCategory == "Scratch")
        {
            result.PrimaryRootCause.Should().Contain("bảo trì");
        }
        else if (expectedCategory == "Deformation")
        {
            result.PrimaryRootCause.Should().Contain("thủy lực");
        }
    }

    [Fact]
    public void SerializationAndDeserialization_RootCauseAnalysisResult_MaintainsFidelity()
    {
        // Arrange
        var original = AiInspectionService.GenerateHeuristicRootCauseAnalysis("Crack", "Kiểm định nứt");

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<RootCauseAnalysisResult>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.FiveWhys.Should().HaveCount(5);
        deserialized.IshikawaCategories.Should().HaveCount(6);
        deserialized.PrimaryRootCause.Should().Be(original.PrimaryRootCause);
        deserialized.RecommendedCorrectiveAction.Should().Be(original.RecommendedCorrectiveAction);
        deserialized.RecommendedPreventiveAction.Should().Be(original.RecommendedPreventiveAction);
    }
}
