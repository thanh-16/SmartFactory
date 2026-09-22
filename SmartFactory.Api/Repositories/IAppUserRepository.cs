using SmartFactory.Api.Models.Entities;

namespace SmartFactory.Api.Repositories;

public interface IAppUserRepository
{
    Task<AppUser?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<AppUser>> GetAllAsync(CancellationToken ct = default);
}
