using SmartFactory.Api.Models.DTOs;

namespace SmartFactory.Api.Services;

public interface INcrService
{
    Task<NcrReportResponse> CreateInspectionReportAsync(NcrInspectionRequest request, CancellationToken ct = default);
    Task<NcrDecisionResponse> ProcessDecisionAsync(NcrDecisionRequest request, CancellationToken ct = default);
    Task<List<NcrReportResponse>> GetAllReportsAsync(CancellationToken ct = default);
    Task<NcrReportResponse?> GetReportByIdAsync(int id, CancellationToken ct = default);
}
