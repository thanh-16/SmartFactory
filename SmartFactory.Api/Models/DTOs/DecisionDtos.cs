using System.ComponentModel.DataAnnotations;

namespace SmartFactory.Api.Models.DTOs;

public class NcrDecisionRequest
{
    [Required]
    public int NcrReportId { get; set; }

    [Required]
    public string Decision { get; set; } = string.Empty; // Rework, Scrap, Concession, Return

    public string? Notes { get; set; }

    [Required]
    public int ApprovedByUserId { get; set; }
}

public class NcrDecisionResponse
{
    public int Id { get; set; }
    public int NcrReportId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int ApprovedByUserId { get; set; }
    public string ApprovedByName { get; set; } = string.Empty;
    public DateTime DecisionDate { get; set; }
    public string ProductionLotStatus { get; set; } = string.Empty;
    public string NcrReportStatus { get; set; } = string.Empty;
}
