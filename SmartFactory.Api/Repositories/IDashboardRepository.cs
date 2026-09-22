using SmartFactory.Api.Models.DTOs;

namespace SmartFactory.Api.Repositories;

public interface IDashboardRepository
{
    Task<DashboardSummaryResponse> GetSummaryAsync(CancellationToken ct = default);
    Task<List<(string DefectType, int Count)>> GetDefectCountsByTypeAsync(CancellationToken ct = default);
}
