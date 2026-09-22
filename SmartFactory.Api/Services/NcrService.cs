using System.Text.Json;
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
    private static readonly SemaphoreSlim _inspectionLock = new(1, 1);

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
        await _inspectionLock.WaitAsync(ct);
        try
        {
            // 1. Verify existence of related entities
            var lot = await _context.ProductionLots
                .Include(productionLot => productionLot.WorkStation)
                .FirstOrDefaultAsync(productionLot => productionLot.Id == request.LotId, ct);

            if (lot == null)
            {
                throw new NotFoundException($"Production lot with ID {request.LotId} not found.");
            }

            var station = await _context.WorkStations
                .AsNoTracking()
                .FirstOrDefaultAsync(stationEntity => stationEntity.Id == request.StationId, ct);

            if (station == null)
            {
                throw new NotFoundException($"Work station with ID {request.StationId} not found.");
            }

            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(userEntity => userEntity.Id == request.ReportedByUserId, ct);

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
                    // Refresh lot to get latest DefectQuantity in case of concurrent inspections
                    await _context.Entry(lot).ReloadAsync(ct);

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
                        RootCauseAnalysisJson = string.IsNullOrWhiteSpace(request.RootCauseAnalysisJson)
                            ? JsonSerializer.Serialize(AiInspectionService.GenerateHeuristicRootCauseAnalysis(request.DefectType, request.Description))
                            : request.RootCauseAnalysisJson,
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
                        RootCauseAnalysisJson = ncrReport.RootCauseAnalysisJson,
                        RootCauseAnalysis = DeserializeRca(ncrReport.RootCauseAnalysisJson),
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
                        WorkStationId = response.WorkStationId,
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
        finally
        {
            _inspectionLock.Release();
        }
    }

    private static readonly SemaphoreSlim _decisionLock = new(1, 1);

    public async Task<NcrDecisionResponse> ProcessDecisionAsync(NcrDecisionRequest request, CancellationToken ct = default)
    {
        await _decisionLock.WaitAsync(ct);
        NcrDecision decision;
        NcrReport ncr;
        AppUser user;
        ProductionLot? lot;

        try
        {
            ncr = await _context.NcrReports
                .Include(report => report.ProductionLot)
                .Include(report => report.Decisions)
                .FirstOrDefaultAsync(report => report.Id == request.NcrReportId, ct)
                ?? throw new NotFoundException($"NCR Report with ID {request.NcrReportId} not found.");

            // Idempotency check: Cannot re-resolve an already resolved NCR
            if (ncr.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException($"NCR Report '{ncr.NcrNumber}' has already been resolved.");
            }

            user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(userEntity => userEntity.Id == request.ApprovedByUserId, ct)
                ?? throw new NotFoundException($"User with ID {request.ApprovedByUserId} not found.");

            if (!user.Role.Equals("Supervisor", StringComparison.OrdinalIgnoreCase) && 
                !user.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException($"Người dùng '{user.FullName}' (Role: {user.Role}) không có thẩm quyền phê duyệt quyết định xử lý sự cố.");
            }

            lot = ncr.ProductionLot;
            if (lot == null)
            {
                lot = await _context.ProductionLots.FirstOrDefaultAsync(lotEntity => lotEntity.Id == ncr.ProductionLotId, ct);
            }

            if (lot != null)
            {
                // Decision mapping:
                // "Rework" => unlocks lot back to "InProgress"
                // "Scrap" => sets lot to "Scrapped"
                // "Concession" / "Return" => sets lot to "Released"
                lot.Status = (request.Decision ?? string.Empty).ToUpperInvariant() switch
                {
                    "REWORK" => "InProgress",
                    "SCRAP" => "Scrapped",
                    "CONCESSION" => "Released",
                    "RETURN" => "Released",
                    _ => "InProgress"
                };
                lot.UpdatedAt = DateTime.UtcNow;
            }

            ncr.Status = "Resolved";

            decision = new NcrDecision
            {
                NcrReportId = ncr.Id,
                ApprovedByUserId = user.Id,
                Decision = request.Decision ?? string.Empty,
                Notes = request.Notes,
                DecisionDate = DateTime.UtcNow
            };

            await _context.NcrDecisions.AddAsync(decision, ct);
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException dbException)
        {
            throw new ConflictException($"NCR Report with ID {request.NcrReportId} has already been resolved.", dbException);
        }
        finally
        {
            _decisionLock.Release();
        }

        var decisionResponse = new NcrDecisionResponse
        {
            Id = decision.Id,
            NcrReportId = ncr.Id,
            Decision = decision.Decision ?? string.Empty,
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
                    Decision = decision.Decision ?? string.Empty,
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
            .Include(report => report.ProductionLot)
            .Include(report => report.WorkStation)
            .Include(report => report.ReportedByUser)
            .Include(report => report.DefectImages)
            .AsNoTracking()
            .OrderByDescending(report => report.CreatedAt)
            .ToListAsync(ct);

        return reports.Select(reportItem => new NcrReportResponse
        {
            Id = reportItem.Id,
            NcrNumber = reportItem.NcrNumber,
            ProductionLotId = reportItem.ProductionLotId,
            LotNumber = reportItem.ProductionLot?.LotNumber ?? string.Empty,
            LotStatus = reportItem.ProductionLot?.Status ?? string.Empty,
            WorkStationId = reportItem.WorkStationId,
            StationCode = reportItem.WorkStation?.Code ?? string.Empty,
            ReportedByUserId = reportItem.ReportedByUserId,
            ReportedByName = reportItem.ReportedByUser?.FullName ?? string.Empty,
            DefectType = reportItem.DefectType,
            Severity = reportItem.Severity,
            Description = reportItem.Description,
            Status = reportItem.Status,
            RootCauseAnalysisJson = reportItem.RootCauseAnalysisJson,
            RootCauseAnalysis = DeserializeRca(reportItem.RootCauseAnalysisJson),
            CreatedAt = reportItem.CreatedAt,
            ImageUrls = reportItem.DefectImages.Select(img => img.ImageUrl).ToList()
        }).ToList();
    }

    public async Task<NcrReportResponse?> GetReportByIdAsync(int id, CancellationToken ct = default)
    {
        var ncrReport = await _context.NcrReports
            .Include(report => report.ProductionLot)
            .Include(report => report.WorkStation)
            .Include(report => report.ReportedByUser)
            .Include(report => report.DefectImages)
            .AsNoTracking()
            .FirstOrDefaultAsync(report => report.Id == id, ct);

        if (ncrReport == null) return null;

        return new NcrReportResponse
        {
            Id = ncrReport.Id,
            NcrNumber = ncrReport.NcrNumber,
            ProductionLotId = ncrReport.ProductionLotId,
            LotNumber = ncrReport.ProductionLot?.LotNumber ?? string.Empty,
            LotStatus = ncrReport.ProductionLot?.Status ?? string.Empty,
            WorkStationId = ncrReport.WorkStationId,
            StationCode = ncrReport.WorkStation?.Code ?? string.Empty,
            ReportedByUserId = ncrReport.ReportedByUserId,
            ReportedByName = ncrReport.ReportedByUser?.FullName ?? string.Empty,
            DefectType = ncrReport.DefectType,
            Severity = ncrReport.Severity,
            Description = ncrReport.Description,
            Status = ncrReport.Status,
            RootCauseAnalysisJson = ncrReport.RootCauseAnalysisJson,
            RootCauseAnalysis = DeserializeRca(ncrReport.RootCauseAnalysisJson),
            CreatedAt = ncrReport.CreatedAt,
            ImageUrls = ncrReport.DefectImages.Select(img => img.ImageUrl).ToList()
        };
    }

    private static RootCauseAnalysisResult? DeserializeRca(string? rcaJson)
    {
        if (string.IsNullOrWhiteSpace(rcaJson)) return null;
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<RootCauseAnalysisResult>(rcaJson, options);
        }
        catch
        {
            return null;
        }
    }
}
