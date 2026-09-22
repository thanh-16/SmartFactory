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
        var lots = await _context.ProductionLots.AsNoTracking().ToListAsync(ct);
        var ncrs = await _context.NcrReports.AsNoTracking().ToListAsync(ct);

        return new DashboardSummaryResponse
        {
            TotalLots = lots.Count,
            InProgressLots = lots.Count(l => l.Status.Equals("InProgress", StringComparison.OrdinalIgnoreCase)),
            LockedLots = lots.Count(l => l.Status.Equals("Locked", StringComparison.OrdinalIgnoreCase)),
            CompletedLots = lots.Count(l => l.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)),
            ScrappedLots = lots.Count(l => l.Status.Equals("Scrapped", StringComparison.OrdinalIgnoreCase)),
            TotalNcrs = ncrs.Count,
            PendingNcrs = ncrs.Count(n => n.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase)),
            ResolvedNcrs = ncrs.Count(n => n.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase))
        };
    }

    public async Task<List<(string DefectType, int Count)>> GetDefectCountsByTypeAsync(CancellationToken ct = default)
    {
        var groups = await _context.NcrReports
            .AsNoTracking()
            .GroupBy(r => r.DefectType)
            .Select(g => new { DefectType = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);

        return groups.Select(g => (g.DefectType, g.Count)).ToList();
    }
}
