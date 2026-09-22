using Microsoft.EntityFrameworkCore;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.DTOs;

namespace SmartFactory.Api.Repositories;

public class DashboardRepository : IDashboardRepository
{
    private readonly FactoryDbContext _context;

    public DashboardRepository(FactoryDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardSummaryResponse> GetSummaryAsync(CancellationToken ct = default)
    {
        var totalLots = await _context.ProductionLots.CountAsync(ct);
        var inProgressLots = await _context.ProductionLots.CountAsync(lot => lot.Status == "InProgress", ct);
        var lockedLots = await _context.ProductionLots.CountAsync(lot => lot.Status == "Locked", ct);
        var completedLots = await _context.ProductionLots.CountAsync(lot => lot.Status == "Completed", ct);
        var scrappedLots = await _context.ProductionLots.CountAsync(lot => lot.Status == "Scrapped", ct);

        var totalNcrs = await _context.NcrReports.CountAsync(ct);
        var pendingNcrs = await _context.NcrReports.CountAsync(ncr => ncr.Status == "Pending", ct);
        var resolvedNcrs = await _context.NcrReports.CountAsync(ncr => ncr.Status == "Resolved", ct);

        return new DashboardSummaryResponse
        {
            TotalLots = totalLots,
            InProgressLots = inProgressLots,
            LockedLots = lockedLots,
            CompletedLots = completedLots,
            ScrappedLots = scrappedLots,
            TotalNcrs = totalNcrs,
            PendingNcrs = pendingNcrs,
            ResolvedNcrs = resolvedNcrs
        };
    }

    public async Task<List<(string DefectType, int Count)>> GetDefectCountsByTypeAsync(CancellationToken ct = default)
    {
        var groups = await _context.NcrReports
            .AsNoTracking()
            .GroupBy(report => report.DefectType)
            .Select(group => new { DefectType = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count)
            .ToListAsync(ct);

        return groups.Select(group => (group.DefectType, group.Count)).ToList();
    }
}
