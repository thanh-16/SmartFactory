using SmartFactory.Api.Models.DTOs;

namespace SmartFactory.Api.Services;

public interface IDashboardService
{
    Task<DashboardSummaryResponse> GetSummaryAsync(CancellationToken ct = default);
    Task<ParetoResponse> GetParetoAnalysisAsync(CancellationToken ct = default);
}
