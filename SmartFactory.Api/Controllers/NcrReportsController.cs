using Microsoft.AspNetCore.Mvc;
using SmartFactory.Api.Exceptions;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Services;

namespace SmartFactory.Api.Controllers;

[ApiController]
[Route("api/ncr-reports")]
public class NcrReportsController : ControllerBase
{
    private readonly INcrService _ncrService;
    private readonly IAiInspectionService _aiService;

    public NcrReportsController(INcrService ncrService, IAiInspectionService aiService)
    {
        _ncrService = ncrService;
        _aiService = aiService;
    }

    [HttpPost("analyze-ai")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AiAnalysisResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> AnalyzeAi([FromForm] string? description, [FromForm] IFormFile? image, CancellationToken ct)
    {
        var fileName = image?.FileName;
        byte[]? imageBytes = null;
        string? mimeType = null;

        if (image != null && image.Length > 0)
        {
            using var ms = new MemoryStream();
            await image.CopyToAsync(ms, ct);
            imageBytes = ms.ToArray();
            mimeType = image.ContentType;
        }

        var result = await _aiService.AnalyzeDefectAsync(description ?? string.Empty, fileName, imageBytes, mimeType, ct);
        return Ok(result);
    }

    [HttpPost("inspect")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(NcrReportResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Inspect([FromForm] NcrInspectionRequest request, CancellationToken ct)
    {
        if (request.Image == null && Request.HasFormContentType && Request.Form.Files.Count > 0)
        {
            request.Image = Request.Form.Files.GetFile("Image") ?? Request.Form.Files[0];
        }

        // Tự động phân tích AI nếu KCS chưa điền loại lỗi hoặc mức độ
        if (string.IsNullOrWhiteSpace(request.DefectType) || request.DefectType == "Auto")
        {
            byte[]? imageBytes = null;
            string? mimeType = null;

            if (request.Image != null && request.Image.Length > 0)
            {
                using var ms = new MemoryStream();
                await request.Image.CopyToAsync(ms, ct);
                imageBytes = ms.ToArray();
                mimeType = request.Image.ContentType;
            }

            var aiAnalysis = await _aiService.AnalyzeDefectAsync(
                request.Description ?? string.Empty, 
                request.Image?.FileName, 
                imageBytes, 
                mimeType, 
                ct);

            request.DefectType = aiAnalysis.DefectType;
            request.Severity = aiAnalysis.Severity;
            request.Description = string.IsNullOrWhiteSpace(request.Description)
                ? $"[AI Phân Tích: {aiAnalysis.RootCauseAnalysis}]"
                : $"{request.Description} (AI: {aiAnalysis.RootCauseAnalysis})";
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _ncrService.CreateInspectionReportAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<NcrReportResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var reports = await _ncrService.GetAllReportsAsync(ct);
        return Ok(reports);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(NcrReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var report = await _ncrService.GetReportByIdAsync(id, ct);
        if (report == null)
        {
            throw new NotFoundException($"NCR Report with ID {id} not found.");
        }
        return Ok(report);
    }
}
