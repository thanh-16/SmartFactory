using System;
using System.Collections.Generic;

namespace SmartFactory.Api.Models.DTOs;

/// <summary>
/// Tải trọng cảnh báo sớm phát qua SignalR WebSocket
/// </summary>
public class PredictiveAlertPayload
{
    public int WorkStationId { get; set; }
    public string StationCode { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public string AlertLevel { get; set; } = "Warning"; // "Warning" (Vàng) hoặc "Critical" (Cam Đậm)
    public string RuleViolated { get; set; } = string.Empty; // "NelsonRule1", "NelsonRule2", "NelsonRule3"
    public string RuleDescription { get; set; } = string.Empty;
    public double CurrentDefectRate { get; set; }
    public double UpperControlLimit { get; set; }
    public string DominantDefectType { get; set; } = string.Empty;
    public string RootCauseHypothesis { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public int AnomalyScore { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Điểm dữ liệu thời gian cho đồ thị SPC
/// </summary>
public class SpcDataPointDto
{
    public DateTime Timestamp { get; set; }
    public double Value { get; set; }
    public double UCL { get; set; }
    public double CenterLine { get; set; }
    public double LCL { get; set; }
    public bool IsAnomaly { get; set; }
    public string? AnomalyReason { get; set; }
}

/// <summary>
/// DTO trả về cho Dashboard SPC của từng trạm máy
/// </summary>
public class StationSpcMetricsDto
{
    public int WorkStationId { get; set; }
    public string StationCode { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public double HistoricalMeanDefectRate { get; set; }
    public double CurrentUcl { get; set; }
    public double CurrentLcl { get; set; }
    public double ProcessCapabilityCpk { get; set; }
    public string ProcessStatus { get; set; } = "InControl"; // "InControl", "Warning", "OutOfControl"
    public List<SpcDataPointDto> RecentPoints { get; set; } = new();
    public PredictiveAlertPayload? ActiveAlert { get; set; }
}

/// <summary>
/// Kết quả đánh giá vi phạm bộ quy tắc Nelson Rules
/// </summary>
public class SpcEvaluationResult
{
    public bool HasViolation { get; set; }
    public string ViolatedRule { get; set; } = "None";
    public string RuleDescription { get; set; } = string.Empty;
    public string AlertLevel { get; set; } = "None"; // "Warning", "Critical"
    public int AnomalyScore { get; set; }
    public string RootCauseHypothesis { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}
