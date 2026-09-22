namespace SmartFactory.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using SmartFactory.Api.Exceptions;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Models.Entities;
using SmartFactory.Api.Repositories;

[ApiController]
[Route("api/work-stations")]
public class WorkStationsController : ControllerBase
{
    private readonly IWorkStationRepository _workStationRepository;
    private readonly IProductionLotRepository _lotRepository;

    public WorkStationsController(
        IWorkStationRepository workStationRepository,
        IProductionLotRepository lotRepository)
    {
        _workStationRepository = workStationRepository;
        _lotRepository = lotRepository;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<WorkStationResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var stations = await _workStationRepository.GetAllAsync(ct);
        var lots = await _lotRepository.GetAllAsync(ct);

        var result = stations.Select(stationItem => new WorkStationResponse
        {
            Id = stationItem.Id,
            Code = stationItem.Code,
            Name = stationItem.Name,
            Description = stationItem.Description,
            IsActive = stationItem.IsActive,
            CreatedAt = stationItem.CreatedAt,
            ActiveLotsCount = lots.Count(lotItem => lotItem.WorkStationId == stationItem.Id && lotItem.Status == "InProgress")
        }).ToList();

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(WorkStationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var station = await _workStationRepository.GetByIdAsync(id, ct);
        if (station == null)
        {
            throw new NotFoundException($"WorkStation with ID {id} not found.");
        }

        var activeLotsCount = await _lotRepository.CountActiveLotsByStationIdAsync(station.Id, ct);

        var response = new WorkStationResponse
        {
            Id = station.Id,
            Code = station.Code,
            Name = station.Name,
            Description = station.Description,
            IsActive = station.IsActive,
            CreatedAt = station.CreatedAt,
            ActiveLotsCount = activeLotsCount
        };

        return Ok(response);
    }
}
