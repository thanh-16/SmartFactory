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
            .Include(r => r.ProductionLot)
            .Include(r => r.WorkStation)
            .Include(r => r.ReportedByUser)
            .Include(r => r.DefectImages)
            .Include(r => r.Decisions)
                .ThenInclude(d => d.ApprovedByUser)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<List<NcrReport>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.NcrReports
            .Include(r => r.ProductionLot)
            .Include(r => r.WorkStation)
            .Include(r => r.ReportedByUser)
            .Include(r => r.DefectImages)
            .Include(r => r.Decisions)
                .ThenInclude(d => d.ApprovedByUser)
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
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
