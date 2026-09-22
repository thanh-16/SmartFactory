namespace SmartFactory.Api.Models.DTOs;

public class ProductionLotResponse
{
    public int Id { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public int WorkStationId { get; set; }
    public string StationCode { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int DefectQuantity { get; set; }
    public double DefectRate { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateProductionLotRequest
{
    public string LotNumber { get; set; } = string.Empty;
    public int WorkStationId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
