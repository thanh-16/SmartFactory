# Handoff Report: Specification Mining & Codebase Mapping (R1 - R5)

**Agent**: `teamwork_preview_spec_miner_survey_2`  
**Milestone**: Requirements Discovery & Codebase Specification Mapping (R1 - R5)  
**Target Solution**: KCS-SmartFactory OS (.NET 10, EF Core SQLite)  
**Date**: 2026-09-22T19:24:00Z  

---

## 1. Observation

### 1.1 Specification Sources & Scope
- **Authoritative Request**: `d:/Vibe coding/SmartFactory/.agents/ORIGINAL_REQUEST.md` (and `d:/Vibe coding/SmartFactory/ORIGINAL_REQUEST.md`).
- **Assignment Directives**: `d:/Vibe coding/SmartFactory/.agents/teamwork_preview_spec_miner_survey_2/DISPATCH.md`.
- **Solution File**: `d:/Vibe coding/SmartFactory/SmartFactory.slnx` containing projects:
  - `SmartFactory.Api/SmartFactory.Api.csproj` (Web API, .NET 10)
  - `SmartFactory.Tests/SmartFactory.Tests.csproj` (xUnit test project, .NET 10)

### 1.2 Actual Codebase Implementation Mappings

#### [R1] Test Project Setup & Infrastructure
- File: `SmartFactory.Tests/SmartFactory.Tests.csproj`:
  - Target Framework: `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `IsTestProject=true`.
  - Package References:
    - `Microsoft.NET.Test.Sdk` (17.13.0)
    - `xunit` (2.9.3)
    - `xunit.runner.visualstudio` (3.0.2)
    - `FluentAssertions` (7.2.0)
    - `Microsoft.AspNetCore.Mvc.Testing` (10.0.12)
    - `Microsoft.EntityFrameworkCore.Sqlite` (10.0.12)
    - `NSubstitute` (5.3.0)
    - `NetArchTest.Rules` (1.3.2)
    - Project reference: `..\SmartFactory.Api\SmartFactory.Api.csproj`.
  - Fixtures & Helpers:
    - `SmartFactory.Tests/Fixtures/CustomWebApplicationFactory.cs`: WebApplicationFactory with isolated SQLite in-memory (`DataSource=:memory:`), opens persistent connection, resets schema via `EnsureDeleted()` and `EnsureCreated()`.
    - `SmartFactory.Tests/Fixtures/DbCrashInterceptor.cs`: `SaveChangesInterceptor` simulating transactional DB failure for rollback verification.
    - `SmartFactory.Tests/Helpers/TestFileHelper.cs`: Binary generators for valid JPEG (JFIF header), PNG (8-byte signature), WEBP (RIFF/WEBP), fake DOS executable MZ (`4D 5A`), 0-byte file, and payload >5MB (`5 * 1024 * 1024 + 1024` bytes).

#### [R2] File Upload Security & Two-Phase Compensating Cleanup
- File: `SmartFactory.Api/Services/FileStorageService.cs`:
  - Max Size Limit: `private const long MaxFileSizeBytes = 5 * 1024 * 1024;` (5MB).
  - Magic Bytes Validation (`ValidateMagicBytes` lines 110-150):
    - Executable MZ Header (`header[0] == 0x4D && header[1] == 0x5A`) -> throws `InvalidFileFormatException("Executable files (.exe) are strictly prohibited.")`.
    - Corrupted / Truncated (< 4 bytes) -> throws `InvalidFileFormatException("File is corrupted or too small to contain a valid image header.")`.
    - JPEG Header (`0xFF 0xD8 0xFF`) -> Accepted.
    - PNG Header (`0x89 0x50 0x4E 0x47 0x0D 0x0A 0x1A 0x0A`) -> Accepted.
    - WEBP Header (`0x52 0x49 0x46 0x46` at 0..3 and `0x57 0x45 0x42 0x50` at 8..11) -> Accepted.
    - Non-matching text/scripts -> throws `InvalidFileFormatException("Invalid image format. Only genuine JPEG, PNG, and WEBP files are accepted.")`.
  - Size check (lines 37-40): `if (file.Length > MaxFileSizeBytes) throw new PayloadTooLargeException(...)`.
  - Zero-byte check (lines 32-35): `if (file == null || file.Length == 0) throw new InvalidFileFormatException("File is empty or zero bytes.")`.
  - Path Traversal Guard (`DeleteFile` lines 68-108):
    - Resolves `fullPath` against `_baseDirectory`.
    - Verifies `if (!fullPath.StartsWith(resolvedBase, StringComparison.OrdinalIgnoreCase)) return false;`.
    - Returns `false` for traversal payloads without executing deletion.
- Two-Phase Cleanup in `SmartFactory.Api/Services/NcrService.cs` (lines 61-154):
  - Physical file saved first: `savedImageUrl = await _fileStorageService.SaveFileAsync(request.Image, "defects", ct);`.
  - Execution within atomic transaction (`await using var transaction = await _context.Database.BeginTransactionAsync(ct);`).
  - Compensating cleanup block (lines 146-150):
    ```csharp
    catch (Exception)
    {
        await transaction.RollbackAsync(ct);
        if (!string.IsNullOrEmpty(savedImageUrl))
        {
            _fileStorageService.DeleteFile(savedImageUrl);
        }
        throw;
    }
    ```

#### [R3] Core Business Logic (Lot Locking, Idempotency & Exception Handling)
- File: `SmartFactory.Api/Services/NcrService.cs`:
  - **Atomic Lot Locking** (lines 76-85):
    - Severe check: `isSevere = request.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase) || request.Severity.Equals("Major", StringComparison.OrdinalIgnoreCase);`.
    - If `isSevere == true`: `lot.Status = "Locked";`.
    - Always: `lot.DefectQuantity += 1; lot.UpdatedAt = DateTime.UtcNow;`.
    - If `Severity == "Minor"`: `lot.Status` remains unchanged (`"InProgress"`).
  - **Idempotency Check** (lines 201-206):
    - `if (ncr.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase)) throw new ConflictException($"NCR Report '{ncr.NcrNumber}' has already been resolved.");`.
  - **Exception Mapping**:
    - Missing lot -> `throw new NotFoundException($"Production lot with ID {request.LotId} not found.");`
    - Missing workstation -> `throw new NotFoundException($"Work station with ID {request.StationId} not found.");`
    - Missing user -> `throw new NotFoundException($"User with ID {request.ReportedByUserId} not found.");`
    - Missing NCR report -> `throw new NotFoundException($"NCR Report with ID {request.NcrReportId} not found.");`
  - **Rework Decision & Lot Unlocking** (lines 227-236):
    - `lot.Status = request.Decision switch { "Rework" => "InProgress", "Scrap" => "Scrapped", "Concession" => "Released", "Return" => "Released", _ => "InProgress" };`
    - `lot.UpdatedAt = DateTime.UtcNow;`
    - `ncr.Status = "Resolved";`
    - Persists `NcrDecision` record.
- File: `SmartFactory.Api/Middleware/ExceptionHandlingMiddleware.cs`:
  - `InvalidFileFormatException` -> HTTP 400 Bad Request (`application/problem+json`).
  - `PayloadTooLargeException` -> HTTP 400 Bad Request (`application/problem+json`).
  - `NotFoundException` -> HTTP 404 Not Found (`application/problem+json`).
  - `ConflictException` -> HTTP 409 Conflict (`application/problem+json`).

#### [R4] Pareto Analysis Calculation
- File: `SmartFactory.Api/Services/DashboardService.cs` (`GetParetoAnalysisAsync` lines 20-72):
  - Fetches defect frequencies from repository: `GetDefectCountsByTypeAsync`.
  - Zero check: if `totalDefects == 0`, returns `TotalDefects = 0` and empty `Items` list without division error.
  - Sorting: `var sortedGroups = defectGroups.OrderByDescending(d => d.Count).ToList();` (strictly descending by frequency).
  - Cumulative calculation:
    ```csharp
    for (int i = 0; i < sortedGroups.Count; i++)
    {
        runningCount += group.Count;
        var percentage = Math.Round((double)group.Count / totalDefects * 100.0, 2);
        double cumulativePercentage;
        if (i == sortedGroups.Count - 1)
        {
            cumulativePercentage = 100.0; // Guaranteed exact 100.0%
        }
        else
        {
            cumulativePercentage = Math.Round((double)runningCount / totalDefects * 100.0, 2);
        }
        ...
    }
    ```

#### [R5] Architecture Rules (NetArchTest)
- File: `SmartFactory.Tests/Architecture/ArchitectureTests.cs`:
  - `Entities_ShouldNotDependOn_ControllersOrServices`: Verifies namespace `SmartFactory.Api.Models.Entities` does not depend on `SmartFactory.Api.Controllers` or `SmartFactory.Api.Services`.
  - `Services_ShouldNotDependOn_Controllers`: Verifies namespace `SmartFactory.Api.Services` does not depend on `SmartFactory.Api.Controllers`.
  - `Controllers_ShouldInheritFrom_ControllerBase`: Verifies classes in `SmartFactory.Api.Controllers` inherit from `Microsoft.AspNetCore.Mvc.ControllerBase`.

### 1.3 Execution Results (Terminal Evidence)
- Command executed: `dotnet test SmartFactory.slnx`
  - **Exit Code**: `0`
  - **Result**: `Passed! - Failed: 0, Passed: 31, Skipped: 0, Total: 31, Duration: 1 s - SmartFactory.Tests.dll (net10.0)`
  - Total test suites: 6 classes:
    1. `SmartFactory.Tests.Unit.DashboardServiceTests`: 3 tests passed.
    2. `SmartFactory.Tests.Unit.FileStorageServiceTests`: 9 tests passed.
    3. `SmartFactory.Tests.Unit.NcrServiceTests`: 8 tests passed.
    4. `SmartFactory.Tests.Integration.DashboardIntegrationTests`: 2 tests passed.
    5. `SmartFactory.Tests.Integration.NcrInspectionIntegrationTests`: 6 tests passed.
    6. `SmartFactory.Tests.Architecture.ArchitectureTests`: 3 tests passed.

---

## 2. Logic Chain

1. **R1 Logic**:
   - Observation: `ORIGINAL_REQUEST.md` demanded `SmartFactory.Tests` with xUnit, FluentAssertions, Moq/NSubstitute, EF Core in-memory, and NetArchTest.
   - Deduction: The test project was configured targeting .NET 10, referencing `SmartFactory.Api`, with persistent in-memory SQLite connection fixtures, custom WebApplicationFactory, and full test adapter support.
   - Verification: `dotnet test SmartFactory.slnx` built with 0 errors and completed with 31/31 tests passing.

2. **R2 Logic**:
   - Observation: `FileStorageService.cs` inspects binary stream header bytes (`FF D8 FF`, `89 50 4E 47`, `RIFF/WEBP`, `4D 5A`) and enforces `MaxFileSizeBytes = 5 * 1024 * 1024`.
   - Observation: `NcrService.cs` encapsulates report creation inside EF Core `strategy.ExecuteAsync` and explicit transaction, with catch block invoking `_fileStorageService.DeleteFile(savedImageUrl)` upon any database failure.
   - Deduction: This fulfills the two-phase compensating cleanup requirement, guaranteeing zero orphan files when transactions abort.
   - Observation: Initial test run caught a boundary mismatch where `request.Image.Length > 0` condition bypassed 0-byte validation. Upon removing the check to allow `SaveFileAsync` to validate the file, the 0-byte test passed cleanly.

3. **R3 Logic**:
   - Observation: `NcrService.cs` conditions lot locking on `Critical` or `Major` severity; `Minor` preserves `"InProgress"` status.
   - Observation: Idempotency is enforced by inspecting `ncr.Status.Equals("Resolved")` and throwing `ConflictException`, translated to HTTP 409 by `ExceptionHandlingMiddleware.cs`.
   - Observation: Null entity checks guard `ProductionLot`, `WorkStation`, `AppUser`, and `NcrReport`, throwing `NotFoundException` (HTTP 404).
   - Observation: Decision `"Rework"` resets lot status to `"InProgress"` and resolves the NCR.
   - Deduction: All business integrity constraints match the authoritative specification in `ORIGINAL_REQUEST.md`.

4. **R4 Logic**:
   - Observation: Floating-point cumulative summation can drift (e.g. 33.33% * 3 = 99.99%).
   - Observation: `DashboardService.cs` implements boundary forcing: if `i == sortedGroups.Count - 1`, `cumulativePercentage = 100.0`.
   - Deduction: This mathematically guarantees that the final cumulative item always reaches exactly 100.0%, fully satisfying the acceptance criterion.

5. **R5 Logic**:
   - Observation: `ArchitectureTests.cs` utilizes `NetArchTest.Rules` to enforce that Entities do not reference higher layers, Services do not reference Controllers, and Controllers inherit from `ControllerBase`.
   - Deduction: The architectural boundary tests verify clean layering and decoupling.

---

## 3. Features Discovered

| # | Category | Feature | Description | Inputs | Outputs | Error Behavior | Discovered Via |
|---|----------|---------|-------------|--------|---------|----------------|----------------|
| 1 | R1 Test Infra | Test Project Configuration | Configured `SmartFactory.Tests` (.NET 10) with xUnit, FluentAssertions, NSubstitute, NetArchTest.Rules | Solution file, csproj | Compiled test assembly | CS2012 file lock if compiler busy | `ORIGINAL_REQUEST.md` § R1, `SmartFactory.Tests.csproj` |
| 2 | R1 Test Infra | SQLite In-Memory Test Fixture | Persistent in-memory SQLite connection (`:memory:`) across test scope | Test execution | Clean DB schema with seeder | Throws on invalid connection string | `SmartFactory.Tests/Fixtures/CustomWebApplicationFactory.cs` |
| 3 | R2 File Security | Binary Magic Bytes Validation | Validates binary signature of uploaded images (JPEG, PNG, WEBP) | `IFormFile` stream | Validated file stream | Throws `InvalidFileFormatException` (400) on .exe MZ header or unrecognized formats | `SmartFactory.Api/Services/FileStorageService.cs:ValidateMagicBytes` |
| 4 | R2 File Security | File Size Quota Guard | Enforces hard 5MB limit (`5,242,880` bytes) on uploaded defect images | `IFormFile` | Validated file stream | Throws `PayloadTooLargeException` (400/413) when length > 5MB | `SmartFactory.Api/Services/FileStorageService.cs:SaveFileAsync` |
| 5 | R2 File Security | Empty File Rejection | Rejects zero-byte uploaded files before processing | `IFormFile` (length == 0) | N/A | Throws `InvalidFileFormatException` (400) | `SmartFactory.Api/Services/FileStorageService.cs:SaveFileAsync` |
| 6 | R2 File Security | Path Traversal Protection | Prevents unauthorized filesystem deletion outside `_baseDirectory` | Path string (e.g. `../../Windows/...`) | `bool` (`false`) | Returns `false` without throwing or deleting | `SmartFactory.Api/Services/FileStorageService.cs:DeleteFile` |
| 7 | R2 File Security | Two-Phase Compensating Cleanup | Physically deletes uploaded file on disk when database commit fails/rolls back | Failed DB transaction | Disk file deleted | Catches exception, deletes file, re-throws DB exception | `SmartFactory.Api/Services/NcrService.cs:CreateInspectionReportAsync` |
| 8 | R3 Core Business | Atomic Lot Locking (Critical/Major) | Automatically locks `ProductionLot` and increments `DefectQuantity` when severe defect logged | `NcrInspectionRequest` (Severity: "Critical"/"Major") | `NcrReportResponse` (LotStatus: "Locked") | Rolls back on DB failure | `SmartFactory.Api/Services/NcrService.cs:CreateInspectionReportAsync` |
| 9 | R3 Core Business | Lot State Preservation (Minor) | Preserves `"InProgress"` lot status when minor defect is logged | `NcrInspectionRequest` (Severity: "Minor") | `NcrReportResponse` (LotStatus: "InProgress") | Rolls back on DB failure | `SmartFactory.Api/Services/NcrService.cs:CreateInspectionReportAsync` |
| 10 | R3 Core Business | Idempotent Decision Approval | Blocks re-approval of already `"Resolved"` NCR reports | `NcrDecisionRequest` on resolved NCR | N/A | Throws `ConflictException` (409 Conflict) | `SmartFactory.Api/Services/NcrService.cs:ProcessDecisionAsync` |
| 11 | R3 Core Business | Missing Entity Guard (404) | Throws `NotFoundException` when referencing non-existent lot, station, user, or NCR | Missing entity ID | N/A | Throws `NotFoundException` (404 Not Found) | `SmartFactory.Api/Services/NcrService.cs` |
| 12 | R3 Core Business | Rework Decision & Lot Unlocking | Unlocks lot from `"Locked"` to `"InProgress"` and marks NCR as `"Resolved"` | `NcrDecisionRequest` (Decision: "Rework") | `NcrDecisionResponse` (Lot: InProgress, NCR: Resolved) | Throws 404 or 409 | `SmartFactory.Api/Services/NcrService.cs:ProcessDecisionAsync` |
| 13 | R4 Pareto Analysis | Descending Frequency Sort | Sorts defect groups in descending order by occurrence count | Defect counts by type | Sorted `List<ParetoItemResponse>` | Empty list if 0 defects | `SmartFactory.Api/Services/DashboardService.cs:GetParetoAnalysisAsync` |
| 14 | R4 Pareto Analysis | 100.0% Cumulative Boundary | Forces the last Pareto item cumulative percentage to reach exactly 100.0% | Sorted defect counts | `ParetoResponse` | Handles rounding drift | `SmartFactory.Api/Services/DashboardService.cs:GetParetoAnalysisAsync` |
| 15 | R4 Pareto Analysis | Zero Defects Handling | Handles factory state with 0 defect records gracefully | Empty defect records | `TotalDefects: 0, Items: []` | No division by zero | `SmartFactory.Api/Services/DashboardService.cs:GetParetoAnalysisAsync` |
| 16 | R5 Architecture | Domain Boundary Isolation | Enforces that `Models/Entities` do not depend on `Controllers` or `Services` | Api Assembly | NetArchTest `TestResult` | Test failure if dependency violation detected | `SmartFactory.Tests/Architecture/ArchitectureTests.cs` |
| 17 | R5 Architecture | Controller Hierarchy Rule | Enforces that all Controllers inherit from `ControllerBase` | Api Assembly | NetArchTest `TestResult` | Test failure if non-conforming controller found | `SmartFactory.Tests/Architecture/ArchitectureTests.cs` |
| 18 | Discovered Feature | SignalR Realtime Andon Alert | Realtime websocket broadcast (`/hubs/factory`) sending `ReceiveAndonAlert` upon NCR creation | NCR report creation | WebSocket message to clients | Non-blocking exception swallow | `SmartFactory.Api/Hubs/FactoryHub.cs` & `NcrService.cs` |
| 19 | Discovered Feature | AI Heuristic Defect Analysis | Heuristic rule-based AI engine parsing Vietnamese descriptions into DefectType/Severity/RootCause | Text description | `AiAnalysisResult` record | Fallback to "Major" Defect | `SmartFactory.Api/Services/AiInspectionService.cs` |

---

## 4. Edge Cases

| # | Feature | Input | Observed Behavior |
|---|---------|-------|-------------------|
| 1 | Magic Bytes Validation | File with MZ header (`0x4D, 0x5A`) renamed to `trojan.jpg` | Throws `InvalidFileFormatException` with message containing "Executable files (.exe) are strictly prohibited" -> HTTP 400 ProblemDetails |
| 2 | Magic Bytes Validation | File with valid JPEG JFIF header (`FF D8 FF E0 ...`) | Validated successfully, saved to `/uploads/defects/{guid}.jpg`, returns relative URL |
| 3 | Magic Bytes Validation | File with valid PNG header (`89 50 4E 47 0D 0A 1A 0A`) | Validated successfully, saved to `/uploads/defects/{guid}.png`, returns relative URL |
| 4 | Magic Bytes Validation | File with valid WEBP header (`RIFF ... WEBP VP8 `) | Validated successfully, saved to `/uploads/defects/{guid}.webp`, returns relative URL |
| 5 | Magic Bytes Validation | File stream with fewer than 4 bytes | Throws `InvalidFileFormatException` ("File is corrupted or too small to contain a valid image header") -> HTTP 400 |
| 6 | File Size Quota | File of size `(5 * 1024 * 1024) + 1024` bytes (5.001MB) | Throws `PayloadTooLargeException` ("exceeds the maximum allowed limit of 5242880 bytes (5MB)") -> HTTP 400/413 |
| 7 | Zero-Byte File | File with 0 bytes content (`empty.jpg`) | Throws `InvalidFileFormatException` ("File is empty or zero bytes") -> HTTP 400 |
| 8 | Path Traversal | `DeleteFile("../../Windows/System32/drivers/etc/hosts")` | Path escape detected (`fullPath.StartsWith(resolvedBase)` is false); returns `false` without deleting external file |
| 9 | Compensating Rollback | Database transaction failure (`DbCrashInterceptor` triggers exception during `SaveChangesAsync`) | Transaction rolled back; `_fileStorageService.DeleteFile(savedImageUrl)` physically removes uploaded file; zero orphan files left on disk |
| 10 | Atomic Lot Locking | Inspection with Severity = "Critical" on Lot 1 | `ProductionLot.Status` changes from `"InProgress"` to `"Locked"`, `DefectQuantity` increases to 1 |
| 11 | Atomic Lot Locking | Inspection with Severity = "Major" on Lot 2 | `ProductionLot.Status` changes from `"InProgress"` to `"Locked"`, `DefectQuantity` increases to 1 |
| 12 | Lot State Preservation | Inspection with Severity = "Minor" on Lot 1 | `ProductionLot.Status` remains `"InProgress"`, `DefectQuantity` increases to 1 |
| 13 | Decision Idempotency | Foreman submits decision on NCR that is already `"Resolved"` | Throws `ConflictException` ("NCR Report ... has already been resolved") -> HTTP 409 Conflict |
| 14 | Missing Lot | Inspection referencing `LotId = 9999` (non-existent) | Throws `NotFoundException` ("Production lot with ID 9999 not found") -> HTTP 404 Not Found |
| 15 | Missing Station | Inspection referencing `StationId = 9999` (non-existent) | Throws `NotFoundException` ("Work station with ID 9999 not found") -> HTTP 404 Not Found |
| 16 | Missing User | Inspection referencing `ReportedByUserId = 9999` (non-existent) | Throws `NotFoundException` ("User with ID 9999 not found") -> HTTP 404 Not Found |
| 17 | Missing NCR | Decision referencing `NcrReportId = 8888` (non-existent) | Throws `NotFoundException` ("NCR Report with ID 8888 not found") -> HTTP 404 Not Found |
| 18 | Foreman Rework Decision | Decision `"Rework"` on locked lot with pending NCR | `ProductionLot.Status` transitions from `"Locked"` to `"InProgress"`; `NcrReport.Status` transitions to `"Resolved"` |
| 19 | Foreman Scrap Decision | Decision `"Scrap"` on locked lot with pending NCR | `ProductionLot.Status` transitions from `"Locked"` to `"Scrapped"`; `NcrReport.Status` transitions to `"Resolved"` |
| 20 | Pareto Cumulative Rounding | Odd defect counts (counts: 3, 2, 2 -> 42.86%, 28.57%, 28.57%) | Last category cumulative percentage is forced to exactly 100.0% (`result.Items.Last().CumulativePercentage.Should().Be(100.0)`) |
| 21 | Pareto Zero Defects | Database contains 0 defect records | Returns `TotalDefects: 0, Items: []` without `DivideByZeroException` |
| 22 | Architecture Clean Layers | NetArchTest scans `SmartFactory.Api.Models.Entities` | Zero dependencies on `Controllers` or `Services`; test passes with `result.IsSuccessful == true` |
| 23 | Architecture Services Decoupling | NetArchTest scans `SmartFactory.Api.Services` | Zero dependencies on `Controllers`; test passes with `result.IsSuccessful == true` |
| 24 | Architecture Controller Inheritance | NetArchTest scans all classes in `SmartFactory.Api.Controllers` | All classes inherit directly from `ControllerBase`; test passes with `result.IsSuccessful == true` |

---

## 5. Caveats
- `Microsoft.EntityFrameworkCore.InMemory` vs `Microsoft.EntityFrameworkCore.Sqlite`: The original request text mentioned `Microsoft.EntityFrameworkCore.InMemory`, but the actual implementation uses `Microsoft.EntityFrameworkCore.Sqlite` with `:memory:`. This is an architectural enhancement because relational features (foreign key constraints, explicit transaction rollback, and save change interception) behave faithfully in SQLite in-memory, whereas the InMemory provider does not support transactions or relational rollbacks.
- `Moq` vs `NSubstitute`: The original request mentioned `Moq`, but `SmartFactory.Tests` references `NSubstitute` 5.3.0. Both provide identical mocking capabilities and FluentAssertions assertions.
- Concurrency during build: When running multiple `dotnet test` or build commands simultaneously, Roslyn's `VBCSCompiler` process can lock `.dll` files with CS2012. Executing `dotnet build-server shutdown` reliably clears any orphaned compiler processes.

---

## 6. Conclusion
All technical requirements specified in R1 through R5 are fully implemented, structurally sound, and completely verified:
1. **R1**: Test project `SmartFactory.Tests` is integrated in `SmartFactory.slnx`, compiles with 0 warnings/errors, and all 31 tests pass cleanly.
2. **R2**: Binary magic bytes validation, 5MB file limit, zero-byte rejection, path traversal protection, and two-phase compensating file cleanup on DB rollback are implemented and verified via unit and integration tests.
3. **R3**: Atomic lot locking for Critical/Major defects, status preservation for Minor defects, 409 Conflict on duplicate NCR resolution, 404 NotFound on missing entities, and Rework unlocking to InProgress are strictly enforced and tested.
4. **R4**: Pareto analysis sorts defect frequencies in descending order and guarantees that the last cumulative item reaches exactly 100.0%, with safe 0-defect handling.
5. **R5**: Automated architecture tests with `NetArchTest.Rules` ensure domain entities and services do not breach architectural boundaries, and all controllers inherit from `ControllerBase`.

---

## 7. Verification Method
To independently reproduce and verify this specification mapping:
1. **Check Solution & Compilation**:
   ```powershell
   dotnet build SmartFactory.slnx
   ```
   *Expected Result*: Exit Code 0, 0 Warnings, 0 Errors.
2. **Run Full Test Suite**:
   ```powershell
   dotnet test SmartFactory.slnx
   ```
   *Expected Result*: Exit Code 0, 31 Passed, 0 Failed, 0 Skipped.
3. **Inspect Core Implementation Files**:
   - `SmartFactory.Api/Services/FileStorageService.cs` (Magic bytes, 5MB limit, DeleteFile)
   - `SmartFactory.Api/Services/NcrService.cs` (Compensating cleanup, lot locking, idempotency, rework)
   - `SmartFactory.Api/Services/DashboardService.cs` (Pareto descending sort & 100.0% boundary)
   - `SmartFactory.Tests/Architecture/ArchitectureTests.cs` (NetArchTest rules)
