using SmartFactory.Api.Models.Entities;

namespace SmartFactory.Api.Repositories;

public interface INcrReportRepository
{
    Task<NcrReport?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<NcrReport>> GetAllAsync(CancellationToken ct = default);
    Task<NcrReport> AddAsync(NcrReport report, CancellationToken ct = default);
    Task UpdateAsync(NcrReport report, CancellationToken ct = default);
}
