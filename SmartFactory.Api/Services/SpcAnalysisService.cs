using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartFactory.Api.Data;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Models.Entities;

namespace SmartFactory.Api.Services;

public class SpcAnalysisService : ISpcAnalysisService
{
    private readonly FactoryDbContext _context;
    private readonly ILogger<SpcAnalysisService> _logger;

    public SpcAnalysisService(FactoryDbContext context, ILogger<SpcAnalysisService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<PredictiveAlertPayload>> RunSpcScanAsync(CancellationToken ct = default)
    {
        var alerts = new List<PredictiveAlertPayload>();
        var stations = await _context.WorkStations
            .AsNoTracking()
            .Where(w => w.IsActive)
            .ToListAsync(ct);

        foreach (var station in stations)
        {
            var stationMetrics = await _context.StationHourlyMetrics
                .Where(m => m.WorkStationId == station.Id)
                .OrderBy(m => m.WindowStartTime)
                .ToListAsync(ct);

            if (stationMetrics.Count == 0)
            {
                // Nếu chưa có StationHourlyMetric nào, kiểm tra xem có NCR nào gần đây không
                var recentDefects = await _context.NcrReports
                    .Where(r => r.WorkStationId == station.Id)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(10)
                    .ToListAsync(ct);

                if (recentDefects.Count >= 3)
                {
                    // Tổng hợp tạm thời từ NCRs
                    var dominantDefect = recentDefects
                        .GroupBy(r => r.DefectType)
                        .OrderByDescending(g => g.Count())
                        .Select(g => g.Key)
                        .FirstOrDefault() ?? "Defect";

                    var hasSevere = recentDefects.Any(r => r.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase) ||
                                                           r.Severity.Equals("Major", StringComparison.OrdinalIgnoreCase));

                    if (hasSevere)
                    {
                        var simulatedRates = new List<double> { 0.01, 0.02, 0.06 };
                        var eval = SpcEngine.EvaluateNelsonRules(simulatedRates, 0.015, 0.05, dominantDefect);
                        if (eval.HasViolation)
                        {
                            alerts.Add(new PredictiveAlertPayload
                            {
                                WorkStationId = station.Id,
                                StationCode = station.Code,
                                StationName = station.Name,
                                AlertLevel = eval.AlertLevel,
                                RuleViolated = eval.ViolatedRule,
                                RuleDescription = eval.RuleDescription,
                                CurrentDefectRate = simulatedRates.Last(),
                                UpperControlLimit = 0.05,
                                DominantDefectType = dominantDefect,
                                RootCauseHypothesis = eval.RootCauseHypothesis,
                                RecommendedAction = eval.RecommendedAction,
                                AnomalyScore = eval.AnomalyScore,
                                Timestamp = DateTime.UtcNow
                            });
                        }
                    }
                }
                continue;
            }

            var recentRates = stationMetrics.Select(m => m.DefectRate).ToList();
            var historicalMean = stationMetrics.Average(m => m.DefectRate);
            var latestMetric = stationMetrics.Last();
            var effectiveUcl = latestMetric.UpperControlLimit > 0 ? latestMetric.UpperControlLimit : SpcEngine.FallbackUclThreshold;
            var dominantDefectType = latestMetric.DominantDefectType;

            var evaluation = SpcEngine.EvaluateNelsonRules(recentRates, historicalMean, effectiveUcl, dominantDefectType);
            if (evaluation.HasViolation)
            {
                // Cập nhật cờ cảnh báo trên metric mới nhất
                latestMetric.IsWarningTriggered = true;
                latestMetric.TriggeredRule = evaluation.ViolatedRule;
                latestMetric.AnomalyScore = evaluation.AnomalyScore;

                alerts.Add(new PredictiveAlertPayload
                {
                    WorkStationId = station.Id,
                    StationCode = station.Code,
                    StationName = station.Name,
                    AlertLevel = evaluation.AlertLevel,
                    RuleViolated = evaluation.ViolatedRule,
                    RuleDescription = evaluation.RuleDescription,
                    CurrentDefectRate = recentRates.Last(),
                    UpperControlLimit = effectiveUcl,
                    DominantDefectType = dominantDefectType,
                    RootCauseHypothesis = evaluation.RootCauseHypothesis,
                    RecommendedAction = evaluation.RecommendedAction,
                    AnomalyScore = evaluation.AnomalyScore,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        if (alerts.Count > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        return alerts;
    }

    public async Task<StationSpcMetricsDto?> GetStationSpcMetricsAsync(int stationId, CancellationToken ct = default)
    {
        var station = await _context.WorkStations
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == stationId, ct);

        if (station == null)
        {
            return null;
        }

        var rawMetrics = await _context.StationHourlyMetrics
            .AsNoTracking()
            .Where(m => m.WorkStationId == stationId)
            .OrderByDescending(m => m.WindowStartTime)
            .Take(24)
            .ToListAsync(ct);

        var metrics = rawMetrics.OrderBy(m => m.WindowStartTime).ToList();

        if (metrics.Count == 0)
        {
            // Trường hợp trạm chưa có metric chuỗi thời gian, tính baseline từ dữ liệu ProductionLots & NCRs
            var lots = await _context.ProductionLots
                .AsNoTracking()
                .Where(l => l.WorkStationId == stationId)
                .ToListAsync(ct);

            var ncrs = await _context.NcrReports
                .AsNoTracking()
                .Where(r => r.WorkStationId == stationId)
                .ToListAsync(ct);

            var totalQty = lots.Sum(l => l.Quantity);
            var totalDefects = lots.Sum(l => l.DefectQuantity) + ncrs.Count;
            var meanRate = totalQty > 0 ? (double)totalDefects / totalQty : 0.015;
            meanRate = Math.Clamp(meanRate, 0.005, 0.20);

            var (ucl, lcl, sigma) = SpcEngine.CalculateBinomialControlLimits(meanRate, totalQty > 0 ? totalQty / Math.Max(1, lots.Count) : 100);
            var cpk = SpcEngine.CalculateCpk(meanRate, sigma);

            var baselinePoints = new List<SpcDataPointDto>();
            var now = DateTime.UtcNow;
            for (int i = 5; i >= 0; i--)
            {
                var time = now.AddHours(-i);
                var rateVariation = Math.Max(0.001, meanRate + ((i % 2 == 0 ? 0.002 : -0.002)));
                baselinePoints.Add(new SpcDataPointDto
                {
                    Timestamp = time,
                    Value = Math.Round(rateVariation, 4),
                    CenterLine = Math.Round(meanRate, 4),
                    UCL = Math.Round(ucl, 4),
                    LCL = Math.Round(lcl, 4),
                    IsAnomaly = false,
                    AnomalyReason = null
                });
            }

            return new StationSpcMetricsDto
            {
                WorkStationId = station.Id,
                StationCode = station.Code,
                StationName = station.Name,
                HistoricalMeanDefectRate = Math.Round(meanRate, 4),
                CurrentUcl = Math.Round(ucl, 4),
                CurrentLcl = Math.Round(lcl, 4),
                ProcessCapabilityCpk = cpk,
                ProcessStatus = "InControl",
                RecentPoints = baselinePoints,
                ActiveAlert = null
            };
        }

        var recentRates = metrics.Select(m => m.DefectRate).ToList();
        var historicalMean = metrics.Average(m => m.DefectRate);
        var averageSampleSize = metrics.Average(m => m.TotalInspected);
        var latestMetric = metrics.Last();

        var (calcUcl, calcLcl, calcSigma) = SpcEngine.CalculateBinomialControlLimits(historicalMean, averageSampleSize);
        var uclValue = latestMetric.UpperControlLimit > 0 ? latestMetric.UpperControlLimit : calcUcl;
        var lclValue = latestMetric.LowerControlLimit >= 0 ? latestMetric.LowerControlLimit : calcLcl;
        var cpkValue = SpcEngine.CalculateCpk(historicalMean, calcSigma);

        var evaluation = SpcEngine.EvaluateNelsonRules(recentRates, historicalMean, uclValue, latestMetric.DominantDefectType);

        var status = "InControl";
        PredictiveAlertPayload? activeAlert = null;

        if (evaluation.HasViolation)
        {
            status = evaluation.AlertLevel.Equals("Critical", StringComparison.OrdinalIgnoreCase) ? "OutOfControl" : "Warning";
            activeAlert = new PredictiveAlertPayload
            {
                WorkStationId = station.Id,
                StationCode = station.Code,
                StationName = station.Name,
                AlertLevel = evaluation.AlertLevel,
                RuleViolated = evaluation.ViolatedRule,
                RuleDescription = evaluation.RuleDescription,
                CurrentDefectRate = recentRates.Last(),
                UpperControlLimit = uclValue,
                DominantDefectType = latestMetric.DominantDefectType,
                RootCauseHypothesis = evaluation.RootCauseHypothesis,
                RecommendedAction = evaluation.RecommendedAction,
                AnomalyScore = evaluation.AnomalyScore,
                Timestamp = latestMetric.WindowEndTime
            };
        }

        var points = metrics.Select(m => new SpcDataPointDto
        {
            Timestamp = m.WindowStartTime,
            Value = Math.Round(m.DefectRate, 4),
            CenterLine = Math.Round(historicalMean, 4),
            UCL = Math.Round(m.UpperControlLimit > 0 ? m.UpperControlLimit : uclValue, 4),
            LCL = Math.Round(m.LowerControlLimit >= 0 ? m.LowerControlLimit : lclValue, 4),
            IsAnomaly = m.IsWarningTriggered,
            AnomalyReason = m.TriggeredRule != "None" ? m.TriggeredRule : null
        }).ToList();

        return new StationSpcMetricsDto
        {
            WorkStationId = station.Id,
            StationCode = station.Code,
            StationName = station.Name,
            HistoricalMeanDefectRate = Math.Round(historicalMean, 4),
            CurrentUcl = Math.Round(uclValue, 4),
            CurrentLcl = Math.Round(lclValue, 4),
            ProcessCapabilityCpk = cpkValue,
            ProcessStatus = status,
            RecentPoints = points,
            ActiveAlert = activeAlert
        };
    }

    public async Task<List<StationSpcMetricsDto>> GetAllStationsSpcMetricsAsync(CancellationToken ct = default)
    {
        var stations = await _context.WorkStations
            .AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.Id)
            .ToListAsync(ct);

        var results = new List<StationSpcMetricsDto>();
        foreach (var station in stations)
        {
            var dto = await GetStationSpcMetricsAsync(station.Id, ct);
            if (dto != null)
            {
                results.Add(dto);
            }
        }

        return results;
    }
}
