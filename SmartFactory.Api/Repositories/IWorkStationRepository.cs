using SmartFactory.Api.Models.Entities;

namespace SmartFactory.Api.Repositories;

public interface IWorkStationRepository
{
    Task<WorkStation?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<WorkStation>> GetAllAsync(CancellationToken ct = default);
}
