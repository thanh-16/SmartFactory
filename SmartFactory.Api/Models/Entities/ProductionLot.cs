namespace SmartFactory.Api.Models.Entities;

public class ProductionLot
{
    public int Id { get; set; }
    public string LotNumber { get; set; } = string.Empty; // LOT-2026-001
    public int WorkStationId { get; set; }
    public WorkStation? WorkStation { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int DefectQuantity { get; set; }
    public string Status { get; set; } = "InProgress"; // InProgress, Locked, Completed, Scrapped
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<NcrReport> NcrReports { get; set; } = new List<NcrReport>();
}
