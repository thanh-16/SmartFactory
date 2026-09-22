using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartFactory.Api.Models.DTOs;

namespace SmartFactory.Api.Services;

public interface ISpcAnalysisService
{
    Task<List<PredictiveAlertPayload>> RunSpcScanAsync(CancellationToken ct = default);
    Task<StationSpcMetricsDto?> GetStationSpcMetricsAsync(int stationId, CancellationToken ct = default);
    Task<List<StationSpcMetricsDto>> GetAllStationsSpcMetricsAsync(CancellationToken ct = default);
}
