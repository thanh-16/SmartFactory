using Microsoft.EntityFrameworkCore;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.Entities;

namespace SmartFactory.Api.Repositories;

public class AppUserRepository : IAppUserRepository
{
    private readonly FactoryDbContext _context;

    public AppUserRepository(FactoryDbContext context)
    {
        _context = context;
    }

    public async Task<AppUser?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<List<AppUser>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.Users.AsNoTracking().ToListAsync(ct);
    }
}
