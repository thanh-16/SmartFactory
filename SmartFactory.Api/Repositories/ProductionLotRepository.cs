using Microsoft.EntityFrameworkCore;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.Entities;

namespace SmartFactory.Api.Repositories;

public class ProductionLotRepository : IProductionLotRepository
{
    private readonly FactoryDbContext _context;

    public ProductionLotRepository(FactoryDbContext context)
    {
        _context = context;
    }

    public async Task<ProductionLot?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.ProductionLots
            .Include(p => p.WorkStation)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<ProductionLot?> GetByLotNumberAsync(string lotNumber, CancellationToken ct = default)
    {
        return await _context.ProductionLots
            .Include(p => p.WorkStation)
            .FirstOrDefaultAsync(p => p.LotNumber == lotNumber, ct);
    }

    public async Task<List<ProductionLot>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.ProductionLots
            .Include(p => p.WorkStation)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<int> CountActiveLotsByStationIdAsync(int stationId, CancellationToken ct = default)
    {
        return await _context.ProductionLots
            .AsNoTracking()
            .CountAsync(p => p.WorkStationId == stationId && (p.Status == "InProgress" || p.Status == "Locked"), ct);
    }

    public async Task<ProductionLot> AddAsync(ProductionLot lot, CancellationToken ct = default)
    {
        await _context.ProductionLots.AddAsync(lot, ct);
        await _context.SaveChangesAsync(ct);
        return lot;
    }

    public async Task UpdateAsync(ProductionLot lot, CancellationToken ct = default)
    {
        _context.ProductionLots.Update(lot);
        await _context.SaveChangesAsync(ct);
    }
}
