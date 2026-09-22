using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Repositories;

namespace SmartFactory.Api.Services;

public class DashboardService : IDashboardService
{
    private readonly IDashboardRepository _dashboardRepository;

    public DashboardService(IDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    public async Task<DashboardSummaryResponse> GetSummaryAsync(CancellationToken ct = default)
    {
        return await _dashboardRepository.GetSummaryAsync(ct);
    }

    public async Task<ParetoResponse> GetParetoAnalysisAsync(CancellationToken ct = default)
    {
        var defectGroups = await _dashboardRepository.GetDefectCountsByTypeAsync(ct);

        var totalDefects = defectGroups.Sum(d => d.Count);
        if (totalDefects == 0)
        {
            return new ParetoResponse
            {
                TotalDefects = 0,
                Items = new List<ParetoItemResponse>()
            };
        }

        // Sort descending by count
        var sortedGroups = defectGroups.OrderByDescending(d => d.Count).ToList();

        var items = new List<ParetoItemResponse>();
        var runningCount = 0;

        for (int i = 0; i < sortedGroups.Count; i++)
        {
            var group = sortedGroups[i];
            runningCount += group.Count;

            var percentage = Math.Round((double)group.Count / totalDefects * 100.0, 2);
            double cumulativePercentage;

            if (i == sortedGroups.Count - 1)
            {
                // The cumulative percentage of the last category must be exactly 100.0%
                cumulativePercentage = 100.0;
            }
            else
            {
                cumulativePercentage = Math.Round((double)runningCount / totalDefects * 100.0, 2);
            }

            items.Add(new ParetoItemResponse
            {
                DefectType = group.DefectType,
                Count = group.Count,
                Percentage = percentage,
                CumulativePercentage = cumulativePercentage
            });
        }

        return new ParetoResponse
        {
            TotalDefects = totalDefects,
            Items = items
        };
    }
}
