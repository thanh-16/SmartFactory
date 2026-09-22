namespace SmartFactory.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using SmartFactory.Api.Exceptions;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Models.Entities;
using SmartFactory.Api.Repositories;

[ApiController]
[Route("api/production-lots")]
public class ProductionLotsController : ControllerBase
{
    private readonly IProductionLotRepository _lotRepository;
    private readonly IWorkStationRepository _workStationRepository;

    public ProductionLotsController(
        IProductionLotRepository lotRepository,
        IWorkStationRepository workStationRepository)
    {
        _lotRepository = lotRepository;
        _workStationRepository = workStationRepository;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<ProductionLotResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] string? status, CancellationToken ct)
    {
        var lots = await _lotRepository.GetAllAsync(ct);

        if (!string.IsNullOrWhiteSpace(status))
        {
            lots = lots.Where(l => l.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var result = lots.Select(MapToResponse).ToList();
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProductionLotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var lot = await _lotRepository.GetByIdAsync(id, ct);
        if (lot == null)
        {
            throw new NotFoundException($"Production Lot with ID {id} not found.");
        }

        return Ok(MapToResponse(lot));
    }

    [HttpGet("by-number/{lotNumber}")]
    [ProducesResponseType(typeof(ProductionLotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByLotNumber(string lotNumber, CancellationToken ct)
    {
        var lot = await _lotRepository.GetByLotNumberAsync(lotNumber, ct);
        if (lot == null)
        {
            throw new NotFoundException($"Production Lot with LotNumber '{lotNumber}' not found.");
        }

        return Ok(MapToResponse(lot));
    }

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ProductionLotResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateProductionLotRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.LotNumber))
        {
            return BadRequest(new ProblemDetails { Detail = "LotNumber is required." });
        }

        if (string.IsNullOrWhiteSpace(request.ProductName))
        {
            return BadRequest(new ProblemDetails { Detail = "ProductName is required." });
        }

        if (request.Quantity <= 0)
        {
            return BadRequest(new ProblemDetails { Detail = "Quantity must be greater than zero." });
        }

        var station = await _workStationRepository.GetByIdAsync(request.WorkStationId, ct);
        if (station == null)
        {
            throw new NotFoundException($"WorkStation with ID {request.WorkStationId} not found.");
        }

        var existing = await _lotRepository.GetByLotNumberAsync(request.LotNumber, ct);
        if (existing != null)
        {
            throw new ConflictException($"Production Lot with number '{request.LotNumber}' already exists.");
        }

        var newLot = new ProductionLot
        {
            LotNumber = request.LotNumber.Trim(),
            ProductName = request.ProductName.Trim(),
            WorkStationId = station.Id,
            Quantity = request.Quantity,
            DefectQuantity = 0,
            Status = "InProgress",
            CreatedAt = DateTime.UtcNow
        };

        var created = await _lotRepository.AddAsync(newLot, ct);
        created.WorkStation = station;

        var response = MapToResponse(created);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, response);
    }

    private static ProductionLotResponse MapToResponse(ProductionLot lot)
    {
        var defectRate = lot.Quantity > 0
            ? Math.Round((double)lot.DefectQuantity * 100.0 / lot.Quantity, 2)
            : 0.0;

        return new ProductionLotResponse
        {
            Id = lot.Id,
            LotNumber = lot.LotNumber,
            WorkStationId = lot.WorkStationId,
            StationCode = lot.WorkStation?.Code ?? string.Empty,
            StationName = lot.WorkStation?.Name ?? string.Empty,
            ProductName = lot.ProductName,
            Quantity = lot.Quantity,
            DefectQuantity = lot.DefectQuantity,
            DefectRate = defectRate,
            Status = lot.Status,
            CreatedAt = lot.CreatedAt,
            UpdatedAt = lot.UpdatedAt
        };
    }
}
