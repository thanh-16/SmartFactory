using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Models.Entities;
using SmartFactory.Tests.Fixtures;
using SmartFactory.Tests.Helpers;
using Xunit;

namespace SmartFactory.Tests.Integration;

[Collection("SequentialIntegrationTests")]
public class Ncr5WhyIshikawaIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Ncr5WhyIshikawaIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateNcr_GeneratesCompleteRootCauseAnalysisJson_AndSavesToDatabase()
    {
        // Arrange
        var jpegBytes = TestFileHelper.CreateValidJpegBytes();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Nứt phôi kim loại kiểm định trạm 1 ISO 9001"), "Description");

        var fileContent = new ByteArrayContent(jpegBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(fileContent, "Image", "rca_crack.jpg");

        // Act
        var response = await _client.PostAsync("/api/ncr-reports/inspect", form);

        // Assert 1: HTTP Response
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await response.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        report.Should().NotBeNull();
        report!.RootCauseAnalysisJson.Should().NotBeNullOrWhiteSpace("Hệ thống phải tự động tạo RCA JSON");

        // Assert 2: Deserialized Object
        report.RootCauseAnalysis.Should().NotBeNull();
        report.RootCauseAnalysis!.FiveWhys.Should().HaveCount(5, "Chuỗi 5-Why phải gồm đủ 5 bước");
        report.RootCauseAnalysis.IshikawaCategories.Should().HaveCount(6, "Mô hình Ishikawa phải chứa đủ 6 nhóm 6M");

        // Assert 3: Database Direct Verification
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
        var dbReport = await db.NcrReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == report.Id);
        dbReport.Should().NotBeNull();
        dbReport!.RootCauseAnalysisJson.Should().NotBeNullOrWhiteSpace();
        dbReport.RootCauseAnalysisJson.Should().ContainEquivalentOf("fiveWhys");
        dbReport.RootCauseAnalysisJson.Should().ContainEquivalentOf("ishikawaCategories");
    }

    [Fact]
    public async Task ExportPdf_WhenNcrHasRootCauseAnalysis_RendersPdfSuccessfully()
    {
        // Arrange: Tạo NCR có RCA
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("2"), "LotId");
        form.Add(new StringContent("2"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Deformation"), "DefectType");
        form.Add(new StringContent("Major"), "Severity");
        form.Add(new StringContent("Biến dạng thanh dẫn hướng cơ khí"), "Description");

        var createRes = await _client.PostAsync("/api/ncr-reports/inspect", form);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var report = await createRes.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);

        // Act: Kết xuất PDF
        var pdfRes = await _client.GetAsync($"/api/ncr-reports/{report!.Id}/export-pdf");

        // Assert
        pdfRes.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfRes.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var pdfBytes = await pdfRes.Content.ReadAsByteArrayAsync();
        pdfBytes.Length.Should().BeGreaterThan(1000);
        var header = System.Text.Encoding.ASCII.GetString(pdfBytes.Take(5).ToArray());
        header.Should().Be("%PDF-");
    }

    [Fact]
    public async Task ExportPdf_WhenLegacyNcrHasNullRootCauseAnalysisJson_RendersPdfWithoutCrashing()
    {
        // Arrange: Tạo một bản ghi NCR cổ điển trực tiếp trong DB với RootCauseAnalysisJson = null
        int legacyNcrId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FactoryDbContext>();
            var legacyNcr = new NcrReport
            {
                NcrNumber = $"NCR-LEGACY-{Guid.NewGuid():N}"[..16],
                ProductionLotId = 3,
                WorkStationId = 3,
                ReportedByUserId = 2,
                DefectType = "Burr",
                Severity = "Minor",
                Description = "Bản ghi sự cố cũ trước Sprint 3 không có dữ liệu RCA",
                Status = "Pending",
                RootCauseAnalysisJson = null, // Giả lập dữ liệu cũ null
                CreatedAt = DateTime.UtcNow
            };

            db.NcrReports.Add(legacyNcr);
            await db.SaveChangesAsync();
            legacyNcrId = legacyNcr.Id;
        }

        // Act: Xuất PDF cho bản ghi cũ
        var response = await _client.GetAsync($"/api/ncr-reports/{legacyNcrId}/export-pdf");

        // Assert: Hệ thống vẫn kết xuất PDF 200 OK mượt mà, tính tương thích ngược tuyệt đối
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var pdfBytes = await response.Content.ReadAsByteArrayAsync();
        pdfBytes.Length.Should().BeGreaterThan(1000);
        var header = System.Text.Encoding.ASCII.GetString(pdfBytes.Take(5).ToArray());
        header.Should().Be("%PDF-");
    }

    [Fact]
    public async Task GetNcrById_ReturnsDeserializedRootCauseAnalysisObject()
    {
        // Arrange: Tạo NCR
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Scratch"), "DefectType");
        form.Add(new StringContent("Minor"), "Severity");
        form.Add(new StringContent("Vết trầy xước nhẹ trên bề mặt"), "Description");

        var createRes = await _client.PostAsync("/api/ncr-reports/inspect", form);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);

        // Act: Truy vấn GET /api/ncr-reports/{id}
        var getRes = await _client.GetAsync($"/api/ncr-reports/{created!.Id}");

        // Assert
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getRes.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
        fetched.Should().NotBeNull();
        fetched!.RootCauseAnalysis.Should().NotBeNull();
        fetched.RootCauseAnalysis!.FiveWhys.Should().HaveCount(5);
        fetched.RootCauseAnalysis.PrimaryRootCause.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetRootCauseEndpoint_WhenNcrExists_Returns200WithRootCauseAnalysis()
    {
        // Arrange
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("1"), "LotId");
        form.Add(new StringContent("1"), "StationId");
        form.Add(new StringContent("2"), "ReportedByUserId");
        form.Add(new StringContent("Crack"), "DefectType");
        form.Add(new StringContent("Critical"), "Severity");
        form.Add(new StringContent("Vết nứt chân đế lan rộng"), "Description");

        var createRes = await _client.PostAsync("/api/ncr-reports/inspect", form);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);

        // Act: GET /api/ncr-reports/{id}/root-cause
        var rcaRes = await _client.GetAsync($"/api/ncr-reports/{created!.Id}/root-cause");

        // Assert
        rcaRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var rca = await rcaRes.Content.ReadFromJsonAsync<RootCauseAnalysisResult>(_jsonOptions);
        rca.Should().NotBeNull();
        rca!.FiveWhys.Should().HaveCount(5);
        rca.IshikawaCategories.Should().HaveCount(6);
        rca.PrimaryRootCause.Should().NotBeNullOrWhiteSpace();
        rca.RecommendedCorrectiveAction.Should().NotBeNullOrWhiteSpace();
        rca.RecommendedPreventiveAction.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetRootCauseEndpoint_WhenNcrNotFound_Returns404ProblemDetails()
    {
        // Act: Non-existent ID
        var response = await _client.GetAsync("/api/ncr-reports/999999/root-cause");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task ParallelExecution_10ThreadsRcaGeneration_ZeroDeadlock()
    {
        // Arrange: 10 parallel inspection submissions across unique lots or stations
        var defectTypes = new[] { "Crack", "Scratch", "Deformation", "Porosity", "Contamination", "Burr", "Other", "Crack", "Scratch", "Deformation" };

        var tasks = defectTypes.Select(async (defectType, index) =>
        {
            var stationId = (index % 3) + 1;
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("1"), "LotId");
            form.Add(new StringContent(stationId.ToString()), "StationId");
            form.Add(new StringContent("2"), "ReportedByUserId");
            form.Add(new StringContent(defectType), "DefectType");
            form.Add(new StringContent("Major"), "Severity");
            form.Add(new StringContent($"Kiểm thử đồng thời luồng {index + 1} loại lỗi {defectType}"), "Description");

            var res = await _client.PostAsync("/api/ncr-reports/inspect", form);
            return res;
        }).ToList();

        // Act
        var responses = await Task.WhenAll(tasks);

        // Assert: 100% of parallel requests succeed with 201 Created without SQLite locking errors
        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created, "Tất cả các luồng đồng thời phải lưu trữ thành công không gặp lỗi database is locked.");
            var report = await response.Content.ReadFromJsonAsync<NcrReportResponse>(_jsonOptions);
            report.Should().NotBeNull();
            report!.RootCauseAnalysis.Should().NotBeNull();
            report.RootCauseAnalysis!.FiveWhys.Should().HaveCount(5);
        }
    }
}
