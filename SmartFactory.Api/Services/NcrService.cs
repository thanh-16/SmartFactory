using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartFactory.Api.Data;
using SmartFactory.Api.Exceptions;
using SmartFactory.Api.Hubs;
using SmartFactory.Api.Models.DTOs;
using SmartFactory.Api.Models.Entities;

namespace SmartFactory.Api.Services;

public class NcrService : INcrService
{
    private readonly FactoryDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly IHubContext<FactoryHub, IFactoryHubClient>? _hubContext;

    public NcrService(
        FactoryDbContext context, 
        IFileStorageService fileStorageService,
        IHubContext<FactoryHub, IFactoryHubClient>? hubContext = null)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _hubContext = hubContext;
    }

    public async Task<NcrReportResponse> CreateInspectionReportAsync(NcrInspectionRequest request, CancellationToken ct = default)
    {
        // 1. Verify existence of related entities
        var lot = await _context.ProductionLots
            .Include(l => l.WorkStation)
            .FirstOrDefaultAsync(l => l.Id == request.LotId, ct);

        if (lot == null)
        {
            throw new NotFoundException($"Production lot with ID {request.LotId} not found.");
        }

        var station = await _context.WorkStations
            .FirstOrDefaultAsync(s => s.Id == request.StationId, ct);

        if (station == null)
        {
            throw new NotFoundException($"Work station with ID {request.StationId} not found.");
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.ReportedByUserId, ct);

        if (user == null)
        {
            throw new NotFoundException($"User with ID {request.ReportedByUserId} not found.");
        }

        // 2. Save physical file if provided
        string? savedImageUrl = null;
        string? originalFileName = null;
        long fileSize = 0;
        string contentType = "image/jpeg";

        if (request.Image != null)
        {
            originalFileName = request.Image.FileName;
            fileSize = request.Image.Length;
            contentType = request.Image.ContentType ?? "image/jpeg";
            savedImageUrl = await _fileStorageService.SaveFileAsync(request.Image, "defects", ct);
        }

        // 3. Resilient atomic transaction with compensating cleanup
        var strategy = _context.Database.CreateExecutionStrategy();

        var response = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            try
            {
                // Atomic lot locking on Major/Critical defect
                var isSevere = request.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase) ||
                               request.Severity.Equals("Major", StringComparison.OrdinalIgnoreCase);

                if (isSevere)
                {
                    lot.Status = "Locked";
                }
                lot.DefectQuantity += 1;
                lot.UpdatedAt = DateTime.UtcNow;

                var ncrNumber = $"NCR-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
                var ncrReport = new NcrReport
                {
                    NcrNumber = ncrNumber,
                    ProductionLotId = lot.Id,
                    WorkStationId = station.Id,
                    ReportedByUserId = user.Id,
                    DefectType = request.DefectType,
                    Severity = request.Severity,
                    Description = request.Description,
                    Status = "Pending",
                    CreatedAt = DateTime.UtcNow
                };

                await _context.NcrReports.AddAsync(ncrReport, ct);
                await _context.SaveChangesAsync(ct);

                if (!string.IsNullOrEmpty(savedImageUrl))
                {
                    var defectImage = new DefectImage
                    {
                        NcrReportId = ncrReport.Id,
                        ImageUrl = savedImageUrl,
                        FileName = originalFileName ?? "image.jpg",
                        FileSizeBytes = fileSize,
                        ContentType = contentType,
                        UploadedAt = DateTime.UtcNow
                    };

                    await _context.DefectImages.AddAsync(defectImage, ct);
                    await _context.SaveChangesAsync(ct);
                }

                await transaction.CommitAsync(ct);

                return new NcrReportResponse
                {
                    Id = ncrReport.Id,
                    NcrNumber = ncrReport.NcrNumber,
                    ProductionLotId = lot.Id,
                    LotNumber = lot.LotNumber,
                    LotStatus = lot.Status,
                    WorkStationId = station.Id,
                    StationCode = station.Code,
                    ReportedByUserId = user.Id,
                    ReportedByName = user.FullName,
                    DefectType = ncrReport.DefectType,
                    Severity = ncrReport.Severity,
                    Description = ncrReport.Description,
                    Status = ncrReport.Status,
                    CreatedAt = ncrReport.CreatedAt,
                    ImageUrls = string.IsNullOrEmpty(savedImageUrl) ? new List<string>() : new List<string> { savedImageUrl }
                };
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(ct);

                // Two-phase compensating cleanup: delete physical file to prevent orphan artifacts
                if (!string.IsNullOrEmpty(savedImageUrl))
                {
                    _fileStorageService.DeleteFile(savedImageUrl);
                }

                throw;
            }
        });

        // Broadcast Realtime Andon Alert to connected dashboards
        if (_hubContext != null)
        {
            try
            {
                var andonPayload = new AndonAlertPayload
                {
                    NcrId = response.Id,
                    NcrNumber = response.NcrNumber,
                    ProductionLotId = response.ProductionLotId,
                    LotNumber = response.LotNumber,
                    ProductName = lot.ProductName,
                    WorkStationId = station.Id,
                    StationCode = station.Code,
                    StationName = station.Name,
                    DefectType = response.DefectType,
                    Severity = response.Severity,
                    Description = response.Description,
                    ImageUrl = response.ImageUrls.FirstOrDefault(),
                    IsLocked = response.LotStatus.Equals("Locked", StringComparison.OrdinalIgnoreCase),
                    ReportedByName = response.ReportedByName,
                    CreatedAt = response.CreatedAt
                };

                await _hubContext.Clients.All.ReceiveAndonAlert(andonPayload);
            }
            catch
            {
                // Non-blocking for SignalR broadcast
            }
        }

        return response;
    }

    public async Task<NcrDecisionResponse> ProcessDecisionAsync(NcrDecisionRequest request, CancellationToken ct = default)
    {
        var ncr = await _context.NcrReports
            .Include(r => r.ProductionLot)
            .Include(r => r.Decisions)
            .FirstOrDefaultAsync(r => r.Id == request.NcrReportId, ct);

        if (ncr == null)
        {
            throw new NotFoundException($"NCR Report with ID {request.NcrReportId} not found.");
        }

        // Idempotency check: Cannot re-resolve an already resolved NCR
        if (ncr.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException($"NCR Report '{ncr.NcrNumber}' has already been resolved.");
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.ApprovedByUserId, ct);

        if (user == null)
        {
            throw new NotFoundException($"User with ID {request.ApprovedByUserId} not found.");
        }

        var lot = ncr.ProductionLot;
        if (lot == null)
        {
            lot = await _context.ProductionLots.FirstOrDefaultAsync(l => l.Id == ncr.ProductionLotId, ct);
        }

        if (lot != null)
        {
            // Decision mapping:
            // "Rework" => unlocks lot back to "InProgress"
            // "Scrap" => sets lot to "Scrapped"
            // "Concession" / "Return" => sets lot to "Released"
            lot.Status = request.Decision switch
            {
                "Rework" => "InProgress",
                "Scrap" => "Scrapped",
                "Concession" => "Released",
                "Return" => "Released",
                _ => "InProgress"
            };
            lot.UpdatedAt = DateTime.UtcNow;
        }

        ncr.Status = "Resolved";

        var decision = new NcrDecision
        {
            NcrReportId = ncr.Id,
            ApprovedByUserId = user.Id,
            Decision = request.Decision,
            Notes = request.Notes,
            DecisionDate = DateTime.UtcNow
        };

        await _context.NcrDecisions.AddAsync(decision, ct);
        await _context.SaveChangesAsync(ct);

        var decisionResponse = new NcrDecisionResponse
        {
            Id = decision.Id,
            NcrReportId = ncr.Id,
            Decision = decision.Decision,
            Notes = decision.Notes,
            ApprovedByUserId = user.Id,
            ApprovedByName = user.FullName,
            DecisionDate = decision.DecisionDate,
            ProductionLotStatus = lot?.Status ?? "Unknown",
            NcrReportStatus = ncr.Status
        };

        if (_hubContext != null)
        {
            try
            {
                var updatePayload = new DecisionUpdatePayload
                {
                    DecisionId = decision.Id,
                    NcrReportId = ncr.Id,
                    NcrNumber = ncr.NcrNumber,
                    Decision = decision.Decision,
                    Notes = decision.Notes,
                    ProductionLotId = lot?.Id ?? 0,
                    LotNumber = lot?.LotNumber ?? string.Empty,
                    LotStatus = lot?.Status ?? "Unknown",
                    NcrStatus = ncr.Status,
                    ApprovedByName = user.FullName,
                    DecisionDate = decision.DecisionDate
                };

                await _hubContext.Clients.All.ReceiveDecisionUpdate(updatePayload);
            }
            catch
            {
                // Non-blocking for SignalR broadcast
            }
        }

        return decisionResponse;
    }

    public async Task<List<NcrReportResponse>> GetAllReportsAsync(CancellationToken ct = default)
    {
        var reports = await _context.NcrReports
            .Include(r => r.ProductionLot)
            .Include(r => r.WorkStation)
            .Include(r => r.ReportedByUser)
            .Include(r => r.DefectImages)
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return reports.Select(r => new NcrReportResponse
        {
            Id = r.Id,
            NcrNumber = r.NcrNumber,
            ProductionLotId = r.ProductionLotId,
            LotNumber = r.ProductionLot?.LotNumber ?? string.Empty,
            LotStatus = r.ProductionLot?.Status ?? string.Empty,
            WorkStationId = r.WorkStationId,
            StationCode = r.WorkStation?.Code ?? string.Empty,
            ReportedByUserId = r.ReportedByUserId,
            ReportedByName = r.ReportedByUser?.FullName ?? string.Empty,
            DefectType = r.DefectType,
            Severity = r.Severity,
            Description = r.Description,
            Status = r.Status,
            CreatedAt = r.CreatedAt,
            ImageUrls = r.DefectImages.Select(i => i.ImageUrl).ToList()
        }).ToList();
    }

    public async Task<NcrReportResponse?> GetReportByIdAsync(int id, CancellationToken ct = default)
    {
        var r = await _context.NcrReports
            .Include(r => r.ProductionLot)
            .Include(r => r.WorkStation)
            .Include(r => r.ReportedByUser)
            .Include(r => r.DefectImages)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (r == null) return null;

        return new NcrReportResponse
        {
            Id = r.Id,
            NcrNumber = r.NcrNumber,
            ProductionLotId = r.ProductionLotId,
            LotNumber = r.ProductionLot?.LotNumber ?? string.Empty,
            LotStatus = r.ProductionLot?.Status ?? string.Empty,
            WorkStationId = r.WorkStationId,
            StationCode = r.WorkStation?.Code ?? string.Empty,
            ReportedByUserId = r.ReportedByUserId,
            ReportedByName = r.ReportedByUser?.FullName ?? string.Empty,
            DefectType = r.DefectType,
            Severity = r.Severity,
            Description = r.Description,
            Status = r.Status,
            CreatedAt = r.CreatedAt,
            ImageUrls = r.DefectImages.Select(i => i.ImageUrl).ToList()
        };
    }
}
