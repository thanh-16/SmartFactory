using Microsoft.EntityFrameworkCore;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.Entities;

namespace SmartFactory.Api.Repositories;

public class WorkStationRepository : IWorkStationRepository
{
    private readonly FactoryDbContext _context;

    public WorkStationRepository(FactoryDbContext context)
    {
        _context = context;
    }

    public async Task<WorkStation?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.WorkStations.AsNoTracking().FirstOrDefaultAsync(workStation => workStation.Id == id, ct);
    }

    public async Task<List<WorkStation>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.WorkStations.AsNoTracking().ToListAsync(ct);
    }
}
