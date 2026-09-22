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
}
