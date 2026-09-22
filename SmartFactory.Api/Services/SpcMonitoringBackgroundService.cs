using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartFactory.Api.Hubs;

namespace SmartFactory.Api.Services;

/// <summary>
/// Dịch vụ nền chạy định kỳ mỗi 15 giây để quét dị thường trôi sai số SPC trên các trạm máy
/// </summary>
public class SpcMonitoringBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SpcMonitoringBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(15);

    public SpcMonitoringBackgroundService(
        IServiceProvider serviceProvider, 
        ILogger<SpcMonitoringBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SpcMonitoringBackgroundService đã khởi động thành công.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var spcService = scope.ServiceProvider.GetRequiredService<ISpcAnalysisService>();
                var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<FactoryHub, IFactoryHubClient>>();

                var alerts = await spcService.RunSpcScanAsync(stoppingToken);

                foreach (var alert in alerts)
                {
                    _logger.LogWarning(
                        "[SPC PREDICTIVE ALERT] Trạm {StationCode}: Vi phạm {Rule} - Mức {Level} - Tỷ lệ lỗi: {Rate:P1}",
                        alert.StationCode, alert.RuleViolated, alert.AlertLevel, alert.CurrentDefectRate);

                    await hubContext.Clients.All.ReceivePredictiveAlert(alert);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong chu kỳ quét SpcMonitoringBackgroundService.");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
