using Microsoft.AspNetCore.Mvc;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Services;

namespace SmartFactory.Api.Controllers;

[ApiController]
[Route("api/ncr-decisions")]
public class NcrDecisionsController : ControllerBase
{
    private readonly INcrService _ncrService;

    public NcrDecisionsController(INcrService ncrService)
    {
        _ncrService = ncrService;
    }

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(NcrDecisionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDecision([FromBody] NcrDecisionRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _ncrService.ProcessDecisionAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
