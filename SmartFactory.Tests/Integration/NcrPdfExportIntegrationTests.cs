using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Tests.Fixtures;
using SmartFactory.Tests.Helpers;
using Xunit;

namespace SmartFactory.Tests.Integration;

public class NcrPdfExportIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public NcrPdfExportIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ExportPdf_WhenNcrExistsWithImage_Returns200OkWithValidPdfBytes()
    {
        // Arrange: Tạo NCR hợp lệ có ảnh đính kèm
        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Nứt phôi kim loại kiểm định trạm ST-01 ISO 9001"), "Description");

        var fileContent = new ByteArrayContent(jpegBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "Image", "crack_defect.jpg");

        var createRes = await _client.PostAsync("/api/ncr-reports/inspect", form);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdReport = await createRes.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        createdReport.Should().NotBeNull();

        // Act: Gửi yêu cầu xuất PDF biên bản NCR
        var response = await _client.GetAsync($"/api/ncr-reports/{createdReport!.Id}/export-pdf");

        // Assert: HTTP 200, Content-Type = application/pdf, Length > 1000 bytes, Magic header %PDF-
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType.Should().NotBeNull();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var pdfBytes = await response.Content.ReadAsByteArrayAsync();
        pdfBytes.Should().NotBeNull();
        pdfBytes.Length.Should().BeGreaterThan(1000, "Tập tin PDF hợp lệ theo chuẩn ISO 9001 phải có dung lượng tối thiểu trên 1000 bytes");

        var pdfHeader = System.Text.Encoding.ASCII.GetString(pdfBytes.Take(5).ToArray());
        pdfHeader.Should().Be("%PDF-", "Dữ liệu trả về phải bắt đầu bằng chữ ký số nhị phân chuẩn PDF (%PDF-)");
    }

    [Fact]
    public async Task ExportPdf_WhenNcrExistsWithoutImage_Returns200OkWithValidPdfBytes()
    {
        // Arrange: Tạo NCR hợp lệ không kèm ảnh
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("DimensionMismatch"), "DefectType");
        form.Add(new StringContent("Minor"), "Severity");
        form.Add(new StringContent("Lỗi sai số kích thước vượt dung sai chuẩn ±0.05mm"), "Description");

        var createRes = await _client.PostAsync("/api/ncr-reports/inspect", form);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdReport = await createRes.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        createdReport.Should().NotBeNull();

        // Act: Gửi yêu cầu xuất PDF khi không có ảnh lỗi
        var response = await _client.GetAsync($"/api/ncr-reports/{createdReport!.Id}/export-pdf");

        // Assert: Vẫn render thành công 200 OK, không crash hay ném NullReferenceException
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var pdfBytes = await response.Content.ReadAsByteArrayAsync();
        pdfBytes.Length.Should().BeGreaterThan(1000);
        var pdfHeader = System.Text.Encoding.ASCII.GetString(pdfBytes.Take(5).ToArray());
        pdfHeader.Should().Be("%PDF-");
    }

    [Fact]
    public async Task ExportPdf_WhenNcrDoesNotExist_Returns404NotFoundProblemDetails()
    {
        // Act: Gửi yêu cầu xuất PDF với ID không tồn tại
        var nonExistentId = 99999;
        var response = await _client.GetAsync($"/api/ncr-reports/{nonExistentId}/export-pdf");

        // Assert: HTTP 404 ProblemDetails chuẩn RFC 7807
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(_jsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)HttpStatusCode.NotFound);
        problem.Title.Should().Be("Resource Not Found");
        problem.Detail.Should().Contain(nonExistentId.ToString());
    }

    [Fact]
    public async Task ExportPdf_WhenNcrIsResolvedWithDecision_RendersApprovedDecisionDetails()
    {
        // Arrange: Tạo NCR và phê duyệt phương án Rework
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("2"), "LotId");
        form.Add(new StringContent("2"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Deformation"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Biến dạng góc phay bề mặt"), "Description");

        var createRes = await _client.PostAsync("/api/ncr-reports/inspect", form);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await createRes.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);

        var decisionRes = await _client.PostAsJsonAsync("/api/ncr-decisions", new NcrDecisionRequest
        {
            NcrReportId = report!.Id,
            Decision = "Rework",
            Notes = "Ủ nhiệt và nắn phẳng bằng máy thủy lực",
            ApprovedByUserId = 1
        });
        decisionRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act: Xuất PDF biên bản khi đã có quyết định xử lý
        var response = await _client.GetAsync($"/api/ncr-reports/{report.Id}/export-pdf");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var pdfBytes = await response.Content.ReadAsByteArrayAsync();
        pdfBytes.Length.Should().BeGreaterThan(1000);
        var pdfHeader = System.Text.Encoding.ASCII.GetString(pdfBytes.Take(5).ToArray());
        pdfHeader.Should().Be("%PDF-");
    }

    [Fact]
    public async Task ExportPdf_WithVietnameseUnicodeAndSpecialCharacters_GeneratesSuccessfully()
    {
        // Arrange: Tạo NCR với văn bản tiếng Việt có dấu đầy đủ & ký hiệu đặc biệt
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("3"), "LotId");
        form.Add(new StringContent("3"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Pore"), "DefectType");
        form.Add(new StringContent("Critical"), "Severity");
        form.Add(new StringContent("Phát hiện rỗ khí nghiêm trọng trên bề mặt cánh cửa chống cháy: Lỗi đúc kim loại nhiệt độ cao!"), "Description");

        var createRes = await _client.PostAsync("/api/ncr-reports/inspect", form);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await createRes.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);

        // Act
        var response = await _client.GetAsync($"/api/ncr-reports/{report!.Id}/export-pdf");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pdfBytes = await response.Content.ReadAsByteArrayAsync();
        pdfBytes.Length.Should().BeGreaterThan(1000);
        var pdfHeader = System.Text.Encoding.ASCII.GetString(pdfBytes.Take(5).ToArray());
        pdfHeader.Should().Be("%PDF-");
    }

    [Fact]
    public async Task ExportPdf_WithDownloadQueryParam_SetsAttachmentContentDisposition()
    {
        // Arrange: Tạo NCR
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Scratch"), "DefectType");
        form.Add(new StringContent("Minor"), "Severity");
        form.Add(new StringContent("Trầy xước nhẹ thanh nhôm định hình"), "Description");

        var createRes = await _client.PostAsync("/api/ncr-reports/inspect", form);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await createRes.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);

        // Act: Yêu cầu tải về qua query param download=true
        var response = await _client.GetAsync($"/api/ncr-reports/{report!.Id}/export-pdf?download=true");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().Contain(".pdf");
    }
}
