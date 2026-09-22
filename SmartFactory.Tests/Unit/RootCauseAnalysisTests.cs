using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Services;
using Xunit;

namespace SmartFactory.Tests.Unit;

public class RootCauseAnalysisTests
{
    private readonly IConfiguration _emptyConfig;

    public RootCauseAnalysisTests()
    {
        _emptyConfig = new ConfigurationBuilder().Build();
    }

    [Theory]
    [InlineData("Crack", "Vết nứt chân đế lan rộng")]
    [InlineData("Scratch", "Trầy xước bề mặt chi tiết")]
    [InlineData("Deformation", "Chi tiết bị cong vênh móp")]
    [InlineData("Porosity", "Rỗ khí mối hàn")]
    [InlineData("Contamination", "Dính dầu mỡ bụi bẩn sơn")]
    [InlineData("Burr", "Mép viền có bavia sắc nhọn")]
    [InlineData("Other", "Sai lệch kích thước chung")]
    public void GenerateHeuristicRootCauseAnalysis_All7DefectTypes_ReturnsValid5WhyAndIshikawa(string defectType, string description)
    {
        // Act
        var result = AiInspectionService.GenerateHeuristicRootCauseAnalysis(defectType, description);

        // Assert: Đủ 5 bước Why tuần tự
        result.Should().NotBeNull();
        result.FiveWhys.Should().HaveCount(5);
        for (int i = 0; i < 5; i++)
        {
            result.FiveWhys[i].Step.Should().Be(i + 1);
            result.FiveWhys[i].Question.Should().NotBeNullOrWhiteSpace();
            result.FiveWhys[i].Answer.Should().NotBeNullOrWhiteSpace();
        }

        // Assert: Đủ 6 danh mục Ishikawa 6M
        result.IshikawaCategories.Should().ContainKeys(
            IshikawaCategoryNames.Man,
            IshikawaCategoryNames.Machine,
            IshikawaCategoryNames.Material,
            IshikawaCategoryNames.Method,
            IshikawaCategoryNames.Measurement,
            IshikawaCategoryNames.Environment
        );

        foreach (var category in result.IshikawaCategories.Values)
        {
            category.Should().NotBeEmpty("Mỗi nhánh xương cá Ishikawa 6M phải có ít nhất 1 nguyên nhân đóng góp");
        }

        // Assert: CAPA và Primary Root Cause
        result.PrimaryRootCause.Should().NotBeNullOrWhiteSpace();
        result.RecommendedCorrectiveAction.Should().NotBeNullOrWhiteSpace();
        result.RecommendedPreventiveAction.Should().NotBeNullOrWhiteSpace();
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.90f);
        result.EngineProvider.Should().Be("Deterministic-Heuristic-6M");
    }

    [Fact]
    public async Task InvestigateRootCauseAsync_WithoutApiKey_ExecutesDeterministicFallback()
    {
        // Arrange
        var service = new AiInspectionService(_emptyConfig, NullLogger<AiInspectionService>.Instance);

        // Act
        var result = await service.InvestigateRootCauseAsync("Crack", "Critical", "Nứt kim loại");

        // Assert
        result.Should().NotBeNull();
        result.FiveWhys.Should().HaveCount(5);
        result.PrimaryRootCause.Should().Contain("Poka-Yoke");
        result.EngineProvider.Should().Be("Deterministic-Heuristic-6M");
    }

    [Fact]
    public async Task InvestigateRootCauseAsync_WithMockGemini_ParsesStructuredOutputSuccessfully()
    {
        // Arrange
        var geminiJsonResponse = @"{
            ""candidates"": [{
                ""content"": {
                    ""parts"": [{
                        ""text"": ""{\""fiveWhys\"": [{\""step\"": 1, \""question\"": \""Q1\"", \""answer\"": \""A1\""}, {\""step\"": 2, \""question\"": \""Q2\"", \""answer\"": \""A2\""}, {\""step\"": 3, \""question\"": \""Q3\"", \""answer\"": \""A3\""}, {\""step\"": 4, \""question\"": \""Q4\"", \""answer\"": \""A4\""}, {\""step\"": 5, \""question\"": \""Q5\"", \""answer\"": \""A5\""}], \""ishikawaCategories\"": {\""Man\"": [\""Lỗi thao tác\""], \""Machine\"": [\""Kẹt trục\""], \""Material\"": [\""Lẫn tạp chất\""], \""Method\"": [\""Tốc độ cao\""], \""Measurement\"": [\""Thước đo lệch\""], \""Environment\"": [\""Ẩm ướt\""]}, \""primaryRootCause\"": \""Nguyên nhân gốc rễ Gemini\"", \""recommendedCorrectiveAction\"": \""Sửa ngay\"", \""recommendedPreventiveAction\"": \""Phòng ngừa ngay\"", \""confidence\"": 0.98}""
                    }]
                }
            }]
        }";

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(geminiJsonResponse, Encoding.UTF8, "application/json")
            });

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Gemini:ApiKey", "AIzaSyFakeKeyForUnitTestingOnly1234567890" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var client = new HttpClient(mockHandler.Object);
        var service = new AiInspectionService(config, NullLogger<AiInspectionService>.Instance, client);

        // Act
        var result = await service.InvestigateRootCauseAsync("Crack", "Critical", "Nứt góc phôi");

        // Assert
        result.Should().NotBeNull();
        result.FiveWhys.Should().HaveCount(5);
        result.PrimaryRootCause.Should().Be("Nguyên nhân gốc rễ Gemini");
        result.EngineProvider.Should().Be("Gemini-1.5-Flash (Cloud Vision)");
    }
}
