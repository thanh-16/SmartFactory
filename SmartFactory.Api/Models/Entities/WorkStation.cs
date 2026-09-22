namespace SmartFactory.Api.Models.Entities;

public class WorkStation
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty; // ST-01, ST-02, ST-03

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string StationCode { get => Code; set => Code = value; }

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProductionLot> ProductionLots { get; set; } = new List<ProductionLot>();
    public ICollection<NcrReport> NcrReports { get; set; } = new List<NcrReport>();
}
