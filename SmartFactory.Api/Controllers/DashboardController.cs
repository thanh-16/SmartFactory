using Microsoft.AspNetCore.Mvc;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Services;

namespace SmartFactory.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(DashboardSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var summary = await _dashboardService.GetSummaryAsync(ct);
        return Ok(summary);
    }

    [HttpGet("pareto")]
    [ProducesResponseType(typeof(ParetoResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPareto(CancellationToken ct)
    {
        var pareto = await _dashboardService.GetParetoAnalysisAsync(ct);
        return Ok(pareto);
    }

    [HttpGet("spc")]
    [ProducesResponseType(typeof(List<StationSpcMetricsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllSpcMetrics(
        [FromServices] ISpcAnalysisService spcService, 
        CancellationToken ct)
    {
        var result = await spcService.GetAllStationsSpcMetricsAsync(ct);
        return Ok(result);
    }

    [HttpGet("spc/{stationId:int}")]
    [ProducesResponseType(typeof(StationSpcMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStationSpc(
        int stationId, 
        [FromServices] ISpcAnalysisService spcService, 
        CancellationToken ct)
    {
        var result = await spcService.GetStationSpcMetricsAsync(stationId, ct);
        if (result == null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Work Station Not Found",
                Detail = $"Work station with ID {stationId} does not exist.",
                Instance = HttpContext.Request.Path
            });
        }
        return Ok(result);
    }
}
