namespace SmartFactory.Api.Hubs;

using Microsoft.AspNetCore.SignalR;

/// <summary>
/// Strongly-typed client contract for real-time factory Andon events
/// </summary>
public interface IFactoryHubClient
{
    Task ReceiveAndonAlert(AndonAlertPayload alert);
    Task ReceiveDecisionUpdate(DecisionUpdatePayload update);
}

/// <summary>
/// SignalR Hub for real-time factory operations, Andon alarms, and supervisor notifications
/// </summary>
public class FactoryHub : Hub<IFactoryHubClient>
{
    public const string DashboardGroup = "SupervisorDashboard";

    public async Task JoinDashboard()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, DashboardGroup);
    }

    public async Task LeaveDashboard()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, DashboardGroup);
    }
}

public class AndonAlertPayload
{
    public int NcrId { get; set; }
    public string NcrNumber { get; set; } = string.Empty;
    public int ProductionLotId { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int WorkStationId { get; set; }
    public string StationCode { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public string DefectType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsLocked { get; set; }
    public string ReportedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class DecisionUpdatePayload
{
    public int DecisionId { get; set; }
    public int NcrReportId { get; set; }
    public string NcrNumber { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int ProductionLotId { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public string LotStatus { get; set; } = string.Empty;
    public string NcrStatus { get; set; } = string.Empty;
    public string ApprovedByName { get; set; } = string.Empty;
    public DateTime DecisionDate { get; set; } = DateTime.UtcNow;
}
