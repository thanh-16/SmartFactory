namespace SmartFactory.Api.Models.Entities;

public class NcrReport
{
    public int Id { get; set; }
    public string NcrNumber { get; set; } = string.Empty; // NCR-2026-001
    public int ProductionLotId { get; set; }
    public ProductionLot? ProductionLot { get; set; }
    public int WorkStationId { get; set; }
    public WorkStation? WorkStation { get; set; }
    public int ReportedByUserId { get; set; }
    public AppUser? ReportedByUser { get; set; }
    public string DefectType { get; set; } = string.Empty; // Crack, Scratch, Deformation, MissingPart, DimensionError
    public string Severity { get; set; } = "Minor"; // Minor, Major, Critical
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Resolved, Closed
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<DefectImage> DefectImages { get; set; } = new List<DefectImage>();
    public ICollection<NcrDecision> Decisions { get; set; } = new List<NcrDecision>();
}
