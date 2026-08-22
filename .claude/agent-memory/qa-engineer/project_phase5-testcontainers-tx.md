---
name: phase5-testcontainers-tx
description: Phase-5 real-Postgres Testcontainers tests for IsRelational-gated tx paths; TenantDataDeletionService has a live BUG-068 prod bug
metadata:
  type: project
---

Phase-5 added self-contained `*PostgresTests.cs` (each spins its own `PostgreSqlContainer("postgres:17-alpine")`, `IAsyncLifetime`) for `IsRelational()`-gated transactional paths that InMemory can't exercise (BUG-068 "InMemory-masks-Postgres" class). No shared base fixture — copy the skeleton from `AuthConcurrentLockoutPostgresTests.cs`. Two new files: `TenantDataDeletionPostgresTests.cs`, `ApplicantConversionPostgresTests.cs`.

**Why:** InMemory has no FK constraints (child-first delete ordering never enforced) and skips `BeginTransactionAsync` (guarded behind `IsRelational()`), so those paths were untested.

**How to apply / key gotchas:**
- Global query filters are `!IsResolved || TenantId==ctx.TenantId`, so a **system context** (`IsResolved=false`) sees every tenant's rows — required for `TenantDataDeletionService` (mirrors the Hangfire job scope). The relational delete path does NOT `IgnoreQueryFilters()`, so with system context the filter collapses to `!IsDeleted` — seed rows with `IsDeleted=false`.
- FK-ordering deletion test seeds a genuine chain: Department + JobTitle → Employee (both FKs `RESTRICT`) → EmployeeDocument (`CASCADE`). The two RESTRICT edges are load-bearing: a wrong child-first order throws a Postgres FK violation. Green = ordering proven.
- **LIVE PROD BUG (BUG-068 class, confirmed in `HRM.Api/Logs/hrm-20260707.log` line 10462, stack `TenantDataDeletionService.DeleteForTypeAsync:150`):** `TenantDataDeletionService.DeleteTenantDataAsync` opens a manual `BeginTransactionAsync` (line 63) WITHOUT `CreateExecutionStrategy().ExecuteAsync(...)`. Prod DI enables `EnableRetryOnFailure` (`DependencyInjection.cs:41`), so every tenant-deletion Hangfire job throws `NpgsqlRetryingExecutionStrategy does not support user-initiated transactions` and rolls back — tenant hard-delete (US-ADM-004 GDPR purge) never actually runs. Fix = same wrapping `ApplicantConversionService` already uses (lines 142-163). To TEST the FK-ordering, the deletion harness disables retry (retry is the separate defect, reported OUT-OF-LANE).
- `ApplicantConversionService` is already fixed — its Postgres test keeps `EnableRetryOnFailure` ON to prove the fix holds under the real retrying strategy.
- `MigrateAsync()` works on this branch (AuthConcurrentLockout uses it and suite is green = no pending model changes); Applicant/Goal concurrency tests use `EnsureCreatedAsync()` only to dodge `PendingModelChangesWarning` when the working tree has uncommitted model drift.
