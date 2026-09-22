namespace SmartFactory.Api.Models.Entities;

public class NcrReport
{
    public int Id { get; set; }
    public string NcrNumber { get; set; } = string.Empty; // NCR-2026-001

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string NcrCode { get => NcrNumber; set => NcrNumber = value; }

    public int ProductionLotId { get; set; }
    public ProductionLot? ProductionLot { get; set; }
    public int WorkStationId { get; set; }
    public WorkStation? WorkStation { get; set; }
    public int ReportedByUserId { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int CreatedByUserId { get => ReportedByUserId; set => ReportedByUserId = value; }

    public AppUser? ReportedByUser { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public AppUser? CreatedByUser { get => ReportedByUser; set => ReportedByUser = value; }

    public string DefectType { get; set; } = string.Empty; // Crack, Scratch, Deformation, MissingPart, DimensionError
    public string Severity { get; set; } = "Minor"; // Minor, Major, Critical
    public string Description { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string OperatorNote { get => Description; set => Description = value; }

    public string Status { get; set; } = "Pending"; // Pending, Resolved, Closed

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string NcrStatus { get => Status; set => Status = value; }

    public string? RootCauseAnalysisJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<DefectImage> DefectImages { get; set; } = new List<DefectImage>();
    public ICollection<NcrDecision> Decisions { get; set; } = new List<NcrDecision>();
}
