# Project: KCS-SmartFactory OS Automated Test Suite

## Architecture
The system is built on .NET 10 (C# 13, ASP.NET Core Web API, EF Core 10 SQLite) following Clean 3-Tier Layering:
- **Presentation Layer**: Controllers (`SmartFactory.Api.Controllers`) inheriting from `ControllerBase`, SignalR Hub (`FactoryHub`), and global RFC 7807 `ExceptionHandlingMiddleware`.
- **Business Logic Layer**: Services (`SmartFactory.Api.Services`) handling validation, business invariants (Lot locking, NCR resolution idempotency, Rework lot unlocking), Pareto statistical computation, and compensating two-phase cleanup.
- **Data Access Layer**: Repositories (`SmartFactory.Api.Repositories`) and `FactoryDbContext` with SQLite relational constraints and seed data.
- **Cross-Cutting**: Custom Domain Exceptions (`SmartFactory.Api.Exceptions`), DTOs (`SmartFactory.Api.Models.DTOs`), Entities (`SmartFactory.Api.Models.Entities`).
- **Test Suite Layer**: `SmartFactory.Tests` containing Unit, Integration, and NetArchTest architectural boundary tests.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | R1: Test Project Setup | Complete `SmartFactory.Tests` configuration with xUnit, FluentAssertions, Moq, InMemory/Sqlite, and NetArchTest.Rules | M1 | ORIGINAL_REQUEST § R1 |
| 2 | R2: Binary Magic Bytes Validation | Validate binary stream headers (JPEG `FF D8 FF`, PNG `89 50 4E 47`, WEBP `RIFF...WEBP`) and reject executable MZ (`4D 5A`) / script formats with `InvalidFileFormatException` | M2 | ORIGINAL_REQUEST § R2 |
| 3 | R2: File Quota & Traversal Security | Hard 5MB limit throwing `PayloadTooLargeException`, reject 0-byte files, and guard `DeleteFile` against path traversal outside upload directory | M2 | ORIGINAL_REQUEST § R2 |
| 4 | R2: Compensating Two-Phase Cleanup | Physically delete disk file when DB transaction aborts or throws during inspection creation | M2 | ORIGINAL_REQUEST § R2 |
| 5 | R3: Atomic Lot Locking | Critical/Major defects lock lot to "Locked" and increment DefectQuantity; Minor defect preserves "InProgress" | M3 | ORIGINAL_REQUEST § R3 |
| 6 | R3: Idempotency & Exceptions | Duplicate decision on "Resolved" NCR throws `ConflictException` (409); missing entities throw `NotFoundException` (404) | M3 | ORIGINAL_REQUEST § R3 |
| 7 | R3: Lot Unlocking on Rework | "Rework" decision transitions lot from "Locked" back to "InProgress" and marks NCR as "Resolved" | M3 | ORIGINAL_REQUEST § R3 |
| 8 | R4: Pareto Analysis Precision | Descending frequency sorting and mathematically enforcing final cumulative percentage at exactly 100.0% | M4 | ORIGINAL_REQUEST § R4 |
| 9 | R5: Architectural Boundary Tests | NetArchTest rules enforcing Entities isolation, Services decoupling from Controllers, and Controllers inheritance from ControllerBase | M5 | ORIGINAL_REQUEST § R5 |
| 10 | R5: DTO Immutability & Encapsulation | NetArchTest rules ensuring DTOs follow encapsulation and immutability standards | M5 | ORIGINAL_REQUEST § R5 |
| 11 | Full Verification & Hardening | 100% test pass rate on `dotnet test`, zero placeholders, zero test skips, clean forensic audit | M6 | ORIGINAL_REQUEST § Acceptance Criteria |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Package Alignment & Project Hardening | Ensure `Moq` (4.20.72) and `Microsoft.EntityFrameworkCore.InMemory` (10.0.12) are explicitly added to `SmartFactory.Tests.csproj` alongside existing packages; verify clean build | none | PLANNED |
| M2 | R2 Security & Two-Phase Cleanup Verification | Validate unit & integration tests for magic bytes, 5MB limit, path traversal, and DB rollback compensating cleanup | M1 | PLANNED |
| M3 | R3 Core Business Logic Verification | Validate unit & integration tests for atomic lot locking, idempotency 409, 404 handling, and rework unlocking | M1 | PLANNED |
| M4 | R4 Pareto Analysis Tests Verification | Validate Pareto sorting and exact 100.0% cumulative percentage tests | M1 | PLANNED |
| M5 | R5 Architecture & DTO Encapsulation Tests | Add NetArchTest rule for DTO encapsulation/immutability to `ArchitectureTests.cs`; verify all architecture tests pass | M1 | PLANNED |
| M6 | Final Verification, Hardening & Forensic Audit | Run full test suite (`dotnet test`), adversarial stress tests, and Forensic Audit gate check | M2, M3, M4, M5 | PLANNED |

## Code Layout
- `SmartFactory.Api/`
  - `Controllers/` - Web API endpoints
  - `Data/` - `FactoryDbContext.cs`
  - `Exceptions/` - `NotFoundException.cs`, `ConflictException.cs`, `InvalidFileFormatException.cs`, `PayloadTooLargeException.cs`
  - `Middleware/` - `ExceptionHandlingMiddleware.cs`
  - `Models/DTOs/` - Request and Response DTO records and classes
  - `Models/Entities/` - Domain entities
  - `Repositories/` - Repository interfaces and EF Core implementations
  - `Services/` - Core business logic services
- `SmartFactory.Tests/`
  - `Architecture/` - `ArchitectureTests.cs` (NetArchTest)
  - `Fixtures/` - `CustomWebApplicationFactory.cs`, `DbCrashInterceptor.cs`
  - `Helpers/` - `TestFileHelper.cs`
  - `Integration/` - Integration tests (`DashboardIntegrationTests.cs`, `NcrInspectionIntegrationTests.cs`)
  - `Unit/` - Unit tests (`DashboardServiceTests.cs`, `FileStorageServiceTests.cs`, `NcrServiceTests.cs`)

## Interface Contracts
- `IFileStorageService`:
  - `Task<string> SaveFileAsync(IFormFile file, string subFolder, CancellationToken ct = default);`
  - `bool DeleteFile(string relativeFilePath);`
- `INcrService`:
  - `Task<NcrReportResponse> CreateInspectionReportAsync(NcrInspectionRequest request, CancellationToken ct = default);`
  - `Task<NcrDecisionResponse> ProcessDecisionAsync(NcrDecisionRequest request, CancellationToken ct = default);`
- `IDashboardService`:
  - `Task<DashboardSummaryResponse> GetSummaryAsync(CancellationToken ct = default);`
  - `Task<ParetoResponse> GetParetoAnalysisAsync(CancellationToken ct = default);`
