# Orchestration Plan: KCS-SmartFactory OS

## Objective
Implement and verify all requirements of KCS-SmartFactory OS (.NET 10, EF Core SQLite) with 100% build pass, 100% test pass, and 0 placeholders.

## Milestones & Strategy
1. **Phase 0: Survey & Scope Mapping**
   - Dispatch 3 Explorers / Spec Miners:
     - Explorer 1: Inspect solution structure, existing .csproj files, entities, DbContext, and Program.cs.
     - Explorer 2: Inspect existing Services, Controllers, Middleware, FileStorage, and API models.
     - Spec Miner 3: Inspect existing SmartFactory.Tests, test suites, failure points, and missing test cases.
   - Synthesize survey findings into `PROJECT.md`.

2. **Milestone 1: Core Service Layer, SQLite DB & API Controllers (R1)**
   - Complete Services: InspectionService, DecisionService, DashboardService, FileStorageService.
   - Complete Controllers: InspectionsController, DecisionsController, DashboardController.
   - Configure SQLite DbContext with WAL mode, DI registration.
   - Configure Global Exception Handling Middleware (returning RFC 7807 ProblemDetails for 400, 404, 409, 413, 500).

3. **Milestone 2: File Upload Security & Two-Phase Cleanup (R2)**
   - Magic Bytes binary header check for JPEG (FF D8 FF), PNG (89 50 4E 47), WEBP (RIFF...WEBP).
   - Reject spoofed files with `InvalidFileFormatException` (400).
   - Reject files > 5MB with `PayloadTooLargeException` (413).
   - Safe GUID file naming and path isolation against Path Traversal.
   - Two-Phase Cleanup: physical deletion of stored file if DB transaction fails/aborts. Keep slow I/O outside DB transaction.

4. **Milestone 3: Core Quality Assurance Business Logic (R3)**
   - Lot Locking: Critical/Major defect reports immediately lock ProductionLot to `Locked` and update DefectQuantity. Minor defect keeps lot `InProgress`.
   - Idempotency: Resolving an already `Resolved` NCR throws `ConflictException` (409 Conflict).
   - Rework: Valid Rework decision unlocks lot back to `InProgress` and resolves NCR to `Resolved`.
   - Pareto Analytics: Cumulative percentage of the final defect category must equal exactly 100.0%. Ordered descending by defect count.
   - Performance: 100% read queries use `.AsNoTracking()`.

5. **Milestone 4: Automated Test Suite (SmartFactory.Tests) (R4)**
   - xUnit, FluentAssertions, Moq, NetArchTest.Rules, EF Core InMemory/SQLite test harness.
   - Unit & integration tests for all business rules, security edge cases, transaction rollback, and architectural boundaries.
   - Run `dotnet build` and `dotnet test` with 100% pass (0 failed, 0 skipped, 0 placeholders).

6. **Quality Gate & Forensics**
   - 2 Reviewers, 2 Challengers, 1 Forensic Auditor.
   - 100% compliance with Zero Placeholder, Hard Verification Gate, and Impact Boundary Mapping.
