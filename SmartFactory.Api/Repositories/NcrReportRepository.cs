using Microsoft.EntityFrameworkCore;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.Entities;

namespace SmartFactory.Api.Repositories;

public class NcrReportRepository : INcrReportRepository
{
    private readonly FactoryDbContext _context;

    public NcrReportRepository(FactoryDbContext context)
    {
        _context = context;
    }

    public async Task<NcrReport?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.NcrReports
            .Include(report => report.ProductionLot)
            .Include(report => report.WorkStation)
            .Include(report => report.ReportedByUser)
            .Include(report => report.DefectImages)
            .Include(report => report.Decisions)
                .ThenInclude(decision => decision.ApprovedByUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(report => report.Id == id, ct);
    }

    public async Task<List<NcrReport>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.NcrReports
            .Include(report => report.ProductionLot)
            .Include(report => report.WorkStation)
            .Include(report => report.ReportedByUser)
            .Include(report => report.DefectImages)
            .Include(report => report.Decisions)
                .ThenInclude(decision => decision.ApprovedByUser)
            .AsNoTracking()
            .OrderByDescending(report => report.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<NcrReport> AddAsync(NcrReport report, CancellationToken ct = default)
    {
        await _context.NcrReports.AddAsync(report, ct);
        await _context.SaveChangesAsync(ct);
        return report;
    }

    public async Task UpdateAsync(NcrReport report, CancellationToken ct = default)
    {
        _context.NcrReports.Update(report);
        await _context.SaveChangesAsync(ct);
    }
}
