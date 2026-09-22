# Project: KCS-SmartFactory OS

## Architecture
- Clean 3-tier architecture: Controllers -> Services -> Repositories -> Data (EF Core SQLite with WAL mode).
- Realtime SignalR Hub (`FactoryHub`) for instant supervisor Andon alerts.
- Global Exception Handling Middleware standardizing all error responses to RFC 7807 `ProblemDetails` (`application/problem+json`).
- Two-Phase Compensating Transaction pattern for uploaded binary files: slow file I/O outside DB transaction, physical file deletion upon DB transaction rollback.
- Thread-safe concurrency control on decision processing with `SemaphoreSlim` and `ConflictException` (HTTP 409) translation.

## Code Layout
- `SmartFactory.Api/`
  - `Controllers/`: `DashboardController.cs`, `NcrReportsController.cs`, `NcrDecisionsController.cs`, `ProductionLotsController.cs`, `WorkStationsController.cs`
  - `Services/`: `FileStorageService.cs`, `NcrService.cs`, `DashboardService.cs`, `AiInspectionService.cs`
  - `Repositories/`: `AppUserRepository.cs`, `WorkStationRepository.cs`, `ProductionLotRepository.cs`, `NcrReportRepository.cs`, `DashboardRepository.cs`
  - `Data/`: `FactoryDbContext.cs`
  - `Models/Entities/`: `AppUser.cs`, `WorkStation.cs`, `ProductionLot.cs`, `NcrReport.cs`, `DefectImage.cs`, `NcrDecision.cs`
  - `Models/DTOs/`: `InspectionDtos.cs`, `DecisionDtos.cs`, `DashboardDtos.cs`, `ProductionLotDtos.cs`, `WorkStationDtos.cs`
  - `Exceptions/`: `NotFoundException.cs`, `ConflictException.cs`, `InvalidFileFormatException.cs`, `PayloadTooLargeException.cs`
  - `Middleware/`: `ExceptionHandlingMiddleware.cs`
  - `Hubs/`: `FactoryHub.cs`
- `SmartFactory.Tests/`
  - `Unit/`: `FileStorageServiceTests.cs`, `NcrServiceTests.cs`, `DashboardServiceTests.cs`
  - `Integration/`: `DashboardIntegrationTests.cs`, `NcrInspectionIntegrationTests.cs`, `FileUploadAdversarialChallengeTests.cs`, `NcrStateMachineStressTests.cs`, `EmpiricalSecurityChallengeTests.cs`
  - `Architecture/`: `ArchitectureTests.cs` (NetArchTest.Rules)
  - `Fixtures/`: `CustomWebApplicationFactory.cs`, `DbCrashInterceptor.cs`
  - `Helpers/`: `TestFileHelper.cs`

## Feature Inventory
| # | Feature | Description | Milestone | Status |
|---|---------|-------------|-----------|--------|
| 1 | Database & Seed | SQLite WAL mode, 6 entities, initial seed | M1 | DONE |
| 2 | Repositories & DbContext | AsNoTracking read queries, Repository pattern | M1 | DONE |
| 3 | Service Layer | Inspection, Decision, Dashboard, FileStorage | M1 | DONE |
| 4 | REST Controllers | Inspections, Decisions, Dashboard, Lots, Stations | M1 | DONE |
| 5 | RFC 7807 ProblemDetails | Global Exception Handling (400, 404, 409, 413, 500) | M1 | DONE |
| 6 | Binary Magic Bytes | JPEG, PNG, WEBP validation, MZ header block | M2 | DONE |
| 7 | File Size Enforcement | Max 5MB file limit with PayloadTooLargeException (HTTP 413) | M2 | DONE |
| 8 | Safe GUID & Path Traversal | Random GUID filename, BaseDirectory path traversal guard | M2 | DONE |
| 9 | Two-Phase Cleanup | Delete physical file on disk when DB transaction rolls back | M2 | DONE |
| 10 | Atomic Lot Locking | Critical/Major -> Locked + DefectQty; Minor -> InProgress | M3 | DONE |
| 11 | NCR Idempotency | ConflictException (409) if resolving already Resolved NCR | M3 | DONE |
| 12 | Rework Lot Unlocking | Valid Rework -> InProgress & NCR Resolved | M3 | DONE |
| 13 | Pareto Analysis 100% | Descending count order, final cumulative % exactly 100.0% | M3 | DONE |
| 14 | SmartFactory.Tests Project | xUnit, FluentAssertions, Moq, InMemory, NetArchTest | M4 | DONE |
| 15 | Unit Tests | FileStorage, NcrService, DashboardService edge cases | M4 | DONE |
| 16 | Integration Tests | HTTP status, ProblemDetails, rollback file cleanup | M4 | DONE |
| 17 | Architecture Tests | NetArchTest rules for layer boundaries and ControllerBase | M4 | DONE |

## Milestones
| # | Name | Scope | Dependencies | Status | Verification Summary |
|---|------|-------|-------------|--------|----------------------|
| 1 | M1: Services, Data & API | Repositories, Services, Controllers, SQLite WAL, RFC 7807 | none | DONE | 0 errors, HTTP 413, 400, 404, 409 verified |
| 2 | M2: File Upload Security | Magic bytes, size <=5MB (HTTP 413), safe GUID, Two-Phase Cleanup | M1 | DONE | Binary headers, polyglot block, rollback cleanup verified |
| 3 | M3: Core Business Logic | Lot locking, Idempotency 409, Rework unlock, Pareto 100% | M1 | DONE | Concurrency lock (10 parallel requests: 1 ok, 9 409), Pareto tail 100% |
| 4 | M4: Automated Test Suite | SmartFactory.Tests 165 tests, Unit, Integration, NetArchTest | M1, M2, M3 | DONE | 165/165 tests passed (100%), 0 failed, 0 skipped, 0 placeholders |

## Verification Gate Summary
- **Reviewer API**: APPROVE
- **Reviewer Code & Architecture**: APPROVE
- **Challenger Stress & Concurrency**: APPROVE
- **Challenger Security & Cleanup**: APPROVE
- **Forensic Integrity Auditor**: CLEAN (0 integrity violations, 0 placeholders, authentic implementations)
- **Gate Result**: PASS
