using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Tests.Fixtures;
using SmartFactory.Tests.Helpers;
using Xunit;

namespace SmartFactory.Tests.Integration;

public class RoleAndUiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RoleAndUiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Role_KcsUser_CanCreateInspectionReportSuccessfully()
    {
        // Arrange: User ID 2 là KCS (Lê Thị KCS)
        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId"); // Role: KCS
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("KCS kiểm định phát hiện vết nứt gia công"), "Description");

        var fileContent = new ByteArrayContent(jpegBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "Image", "kcs_crack.jpg");

        // Act: KCS gửi báo cáo kiểm định
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert: KCS có toàn quyền tạo biên bản và kích hoạt phong tỏa lô
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await response.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        report.Should().NotBeNull();
        report!.ReportedByName.Should().Contain("KCS");
        report.LotStatus.Should().Be("Locked");
    }

    [Fact]
    public async Task Role_SupervisorUser_CanApproveNcrDecision_AndUnlockLot()
    {
        // Arrange: Tạo NCR trước
        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("2"), "LotId");
        form.Add(new StringContent("2"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Deformation"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Biến dạng mối hàn cần duyệt phương án"), "Description");

        var fileContent = new ByteArrayContent(jpegBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "Image", "deform.jpg");

        var inspectRes = await _client.PostAsync("/api/ncr-reports/inspect", form);
        inspectRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await inspectRes.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);

        // Act: Quản đốc (ID 1 - Trần Văn Quản Đốc, Role: Supervisor) duyệt quyết định Rework
        var decisionRequest = new NcrDecisionRequest
        {
            NcrReportId = report!.Id,
            Decision = "Rework",
            Notes = "Ủ nhiệt giải tỏa ứng suất và hiệu chỉnh phôi",
            ApprovedByUserId = 1 // Role: Supervisor
        };

        var response = await _client.PostAsJsonAsync("/api/ncr-decisions", decisionRequest);

        // Assert: Quản đốc có đầy đủ quyền hạn phê duyệt và giải phóng lô hàng
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var decision = await response.Content.ReadFromJsonAsync<NcrDecisionResponse>(_jsonOptions);
        decision.Should().NotBeNull();
        decision!.Decision.Should().Be("Rework");
        decision.ProductionLotStatus.Should().Be("InProgress");
        decision.NcrReportStatus.Should().Be("Resolved");
    }

    [Fact]
    public async Task Role_ManagerUser_CanAccessAllDashboardKpisAndParetoReports()
    {
        // Act: Ban Giám Đốc truy xuất Dashboard Summary & Pareto Analytics
        var summaryRes = await _client.GetAsync("/api/dashboard/summary");
        var paretoRes = await _client.GetAsync("/api/dashboard/pareto");

        // Assert
        summaryRes.StatusCode.Should().Be(HttpStatusCode.OK);
        paretoRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await summaryRes.Content.ReadFromJsonAsync<DashboardSummaryResponse>(_jsonOptions);
        summary.Should().NotBeNull();
        summary!.TotalLots.Should().BeGreaterThan(0);

        var pareto = await paretoRes.Content.ReadFromJsonAsync<ParetoResponse>(_jsonOptions);
        pareto.Should().NotBeNull();
        pareto!.TotalDefects.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void WebUI_IndexHtml_ContainsCoreElementsAndViews()
    {
        // Arrange: Đọc tệp giao diện Web tĩnh wwwroot/index.html
        var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        if (!Directory.Exists(webRoot))
        {
            webRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
        }

        // Tìm kiếm tệp index.html trong dự án SmartFactory.Api
        var apiPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SmartFactory.Api", "wwwroot", "index.html"));
        if (!File.Exists(apiPath))
        {
            apiPath = Path.Combine(webRoot, "index.html");
        }

        File.Exists(apiPath).Should().BeTrue("Tệp wwwroot/index.html phải tồn tại trong dự án Web API");
        var htmlContent = File.ReadAllText(apiPath);

        // Assert: Kiểm tra các thành phần cốt lõi của giao diện
        htmlContent.Should().Contain("KCS-SmartFactory OS");
        htmlContent.Should().Contain("tab-kcs", "Giao diện phải có tab dành riêng cho KCS Hiện Trường");
        htmlContent.Should().Contain("tab-supervisor", "Giao diện phải có tab dành riêng cho Quản Đốc Xưởng (Andon)");
        htmlContent.Should().Contain("decision-modal", "Giao diện phải có Modal phê duyệt xử lý sự cố cho Quản Đốc");
        htmlContent.Should().Contain("paretoChart", "Giao diện phải tích hợp biểu đồ Pareto 80/20");
    }
}
