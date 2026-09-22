using SmartFactory.Api.Models.Entities;

namespace SmartFactory.Api.Repositories;

public interface IProductionLotRepository
{
    Task<ProductionLot?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ProductionLot?> GetByLotNumberAsync(string lotNumber, CancellationToken ct = default);
    Task<List<ProductionLot>> GetAllAsync(CancellationToken ct = default);
    Task<ProductionLot> AddAsync(ProductionLot lot, CancellationToken ct = default);
    Task UpdateAsync(ProductionLot lot, CancellationToken ct = default);
}
