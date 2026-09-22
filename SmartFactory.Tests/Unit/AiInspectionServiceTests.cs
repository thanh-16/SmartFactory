using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using SmartFactory.Api.Services;
using Xunit;

namespace SmartFactory.Tests.Unit;

public class AiInspectionServiceTests
{
    private readonly IConfiguration _emptyConfig;

    public AiInspectionServiceTests()
    {
        var configBuilder = new ConfigurationBuilder();
        _emptyConfig = configBuilder.Build();
    }

    [Theory]
    [InlineData("Phát hiện vết nứt chân đế", "Crack", "Critical")]
    [InlineData("Bề mặt bị trầy xước nhẹ", "Scratch", "Minor")]
    [InlineData("Chi tiết bị biến dạng cong vênh", "Deformation", "Major")]
    [InlineData("Thiếu ốc vít và linh kiện chốt", "MissingPart", "Critical")]
    [InlineData("Lỗi quang học chưa xác định", "Other", "Major")]
    public async Task AnalyzeDefectAsync_WithoutApiKey_ExecutesSmartFallbackCorrectly(
        string description, 
        string expectedDefect, 
        string expectedSeverity)
    {
        // Arrange
        var service = new AiInspectionService(_emptyConfig, NullLogger<AiInspectionService>.Instance);

        // Act
        var result = await service.AnalyzeDefectAsync(description);

        // Assert
        result.Should().NotBeNull();
        result.DefectType.Should().Be(expectedDefect);
        result.Severity.Should().Be(expectedSeverity);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.85f);
        result.Provider.Should().Contain("SmartHeuristicFallback");
        result.RootCauseAnalysis.Should().NotBeNullOrWhiteSpace();
        result.SuggestedAction.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AnalyzeDefectAsync_WithMockGeminiSuccess_ParsesMultimodalResponse()
    {
        // Arrange
        var geminiJsonResponse = @"{
            ""candidates"": [{
                ""content"": {
                    ""parts"": [{
                        ""text"": ""{\""defectType\"": \""Crack\"", \""severity\"": \""Critical\"", \""confidence\"": 0.98, \""rootCauseAnalysis\"": \""Vết nứt sâu do nhiệt độ đúc vượt ngưỡng.\"", \""suggestedAction\"": \""Lập tức cách ly lô sản xuất và kiểm tra áp suất lò.\""}""
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

        var dummyImageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };

        // Act
        var result = await service.AnalyzeDefectAsync("Nứt góc phôi", "crack.jpg", dummyImageBytes, "image/jpeg");

        // Assert
        result.Should().NotBeNull();
        result.DefectType.Should().Be("Crack");
        result.Severity.Should().Be("Critical");
        result.Confidence.Should().Be(0.98f);
        result.Provider.Should().Be("Gemini-1.5-Flash (Cloud Vision)");
        result.RootCauseAnalysis.Should().Contain("nhiệt độ đúc");
    }

    [Fact]
    public async Task AnalyzeDefectAsync_WhenGeminiHttpFails_GracefullyFallsBackToSmartHeuristic()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.ServiceUnavailable, // 503
                Content = new StringContent("Service Overloaded", Encoding.UTF8, "text/plain")
            });

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Gemini:ApiKey", "AIzaSyFakeKeyForUnitTestingOnly1234567890" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var client = new HttpClient(mockHandler.Object);
        var service = new AiInspectionService(config, NullLogger<AiInspectionService>.Instance, client);

        // Act
        var result = await service.AnalyzeDefectAsync("Vết nứt chân đế", "crack.jpg");

        // Assert: Không throw exception, tự động fallback an toàn
        result.Should().NotBeNull();
        result.DefectType.Should().Be("Crack");
        result.Severity.Should().Be("Critical");
        result.Provider.Should().Contain("SmartHeuristicFallback");
    }
}
