namespace SmartFactory.Api.Models.Entities;

public class NcrDecision
{
    public int Id { get; set; }
    public int NcrReportId { get; set; }
    public NcrReport? NcrReport { get; set; }
    public int ApprovedByUserId { get; set; }
    public AppUser? ApprovedByUser { get; set; }
    public string Decision { get; set; } = string.Empty; // Rework, Scrap, Concession, Return
    public string? Notes { get; set; }
    public DateTime DecisionDate { get; set; } = DateTime.UtcNow;
}
