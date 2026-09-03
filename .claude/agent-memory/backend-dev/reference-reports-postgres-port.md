---
name: reference-reports-postgres-port
description: E3 slice 1 — reports suites ported to Testcontainers; shared PostgresContainerFixture cuts ~20s/test to ~0.5s; per-test-container is the repo default and is why the gate is slow
metadata:
  type: reference
---

**The repo's `*PostgresTests` shape starts one container PER TEST, not per class.** xUnit constructs a new
test-class instance for every `[Fact]`, so `IAsyncLifetime` on the test class re-runs `StartAsync` +
`MigrateAsync` per method. Measured 2026-09-03: **~20s per test** (4 tests = 79s wall, 2s of it actual test
time). 92 files use `new PostgreSqlBuilder(...)` this way — a large share of the 20-35 min backend gate.

**Fix used in E3 slice 1:** `Integration/PostgresContainerFixture.cs` + `IClassFixture<PostgresContainerFixture>`.
Container start + `MigrateAsync` move to once per CLASS; xUnit still gives each test a fresh test-class
instance, so per-test `Guid.NewGuid()` tenant fields still isolate tests **through the tenant global query
filter** rather than through a fresh database. Same pilot: 79s → 40s wall, 2s test time.

**When NOT to use it:** an arm that queries cross-tenant (`IgnoreQueryFilters`) or asserts a GLOBAL count
cannot share a database — a sibling's rows change the number. `HrReportExportCleanupPostgresTests` was split
out for exactly this and keeps the per-class-container shape. Check for `IgnoreQueryFilters` before sharing.

**Porting InMemory → Npgsql always needs seed repair, never assertion repair:** InMemory enforces no FKs, so
seeds carried `JobTitleId = Guid.NewGuid()` and no `Tenant` row. Add real `Tenant` + `JobTitle` rows and make
per-test-unique any column with a unique index (subdomain, employee email) once a DB is shared.

**Mutation-proving a provider swap:** `Replace("Database=", "Database=nope_")` on the Testcontainers
connection string stayed GREEN — an unreliable/no-op mutation. The decisive one is returning a **constant
bogus connection string** from the fixture: 4/4 RED with an Npgsql socket failure at the fixture's
`MigrateAsync`. Use that form. See [[feedback-guards-must-be-mutation-proven]],
[[feedback-mutation-check-revert-before-report]], [[feedback-integration-tests-inmemory]].
