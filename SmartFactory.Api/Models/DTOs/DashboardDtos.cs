namespace SmartFactory.Api.Models.DTOs;

public class DashboardSummaryResponse
{
    public int TotalLots { get; set; }
    public int InProgressLots { get; set; }
    public int LockedLots { get; set; }
    public int CompletedLots { get; set; }
    public int ScrappedLots { get; set; }
    public int TotalNcrs { get; set; }
    public int PendingNcrs { get; set; }
    public int ResolvedNcrs { get; set; }
}

public class ParetoItemResponse
{
    public string DefectType { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
    public double CumulativePercentage { get; set; }
}

public class ParetoResponse
{
    public int TotalDefects { get; set; }
    public List<ParetoItemResponse> Items { get; set; } = new();
}
