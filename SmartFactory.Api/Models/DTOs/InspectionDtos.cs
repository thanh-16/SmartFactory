using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace SmartFactory.Api.Models.DTOs;

public class NcrInspectionRequest
{
    [Required]
    public int LotId { get; set; }

    [Required]
    public int StationId { get; set; }

    [Required]
    public int ReportedByUserId { get; set; }

    [Required]
    public string DefectType { get; set; } = string.Empty;

    [Required]
    public string Severity { get; set; } = "Minor"; // Minor, Major, Critical

    public string Description { get; set; } = string.Empty;

    public IFormFile? Image { get; set; }

    public string? RootCauseAnalysisJson { get; set; }
}

public class NcrReportResponse
{
    public int Id { get; set; }
    public string NcrNumber { get; set; } = string.Empty;
    public int ProductionLotId { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public string LotStatus { get; set; } = string.Empty;
    public int WorkStationId { get; set; }
    public string StationCode { get; set; } = string.Empty;
    public int ReportedByUserId { get; set; }
    public string ReportedByName { get; set; } = string.Empty;
    public string DefectType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? RootCauseAnalysisJson { get; set; }
    public RootCauseAnalysisResult? RootCauseAnalysis { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> ImageUrls { get; set; } = new();
}
