---
name: postgres-proof-arm-conventions
description: How to add a real-Postgres proof arm for a projection currently only covered by EF InMemory (ISSUE-427 shape) — shared fixture, FK-valid seeding, unique-index traps, atomic falsification
metadata:
  type: project
---

Adding a "real Postgres proves what InMemory cannot" arm (ISSUE-364 -> ISSUE-427 shape).

**Why:** EF InMemory is a LINQ-to-Objects provider. `GroupBy`/`Contains`/string-concat projections
client-evaluate there, so a query that fails to translate to SQL goes GREEN on InMemory and throws in
production. InMemory also ignores FKs and unique indexes entirely.

**How to apply:**

- **Shape:** `IClassFixture<PostgresContainerFixture>, IAsyncLifetime` — fixture for the container
  (migrations once per CLASS), `IAsyncLifetime` for per-test seeding. Template:
  `src/backend/HRM.Tests/Integration/HrReportPostgresTests.cs:34`. Never a per-test
  `PostgreSqlBuilder` container (~20s/test; that is the shape ISSUE-453 is retiring — several classes
  such as `LeaveUtilizationDepartmentMergePostgresTests` still use it).
- **Hard constraint:** a class using the shared fixture may NOT call `IgnoreQueryFilters()` —
  `SharedPostgresFixtureIsolationTests` (HRM.ArchitectureTests, syntax-tree based) fails the build.
  Tenant-scoped arms never need it. An arm that genuinely needs a cross-tenant count keeps its own
  per-class container.
- **Sharing the DB is a FEATURE for isolation arms:** sibling tenants' rows are physically present,
  so a query-filter leak actually fails instead of being masked by a pristine database. Always scope
  assertions by a per-test key (`Single(d => d.Code == ...)`), never by a bare table count.
- **FK-valid seeding order:** Tenant + JobTitle first (InitializeAsync), then Department, then
  Employee (`DepartmentId` is REQUIRED and non-nullable). `Department.ManagerId` -> Employee and
  `Employee.DepartmentId` -> Department is a cycle: insert the department, then the employee, then
  UPDATE the department's `ManagerId`. One insert cannot do it.
- **Unique-index trap (cost me a red run):** `BaseEntity.NewUuidV7()` is time-ordered, so its leading
  hex is a TIMESTAMP. Deriving `EmployeeNo` from it collides within the same millisecond ->
  `23505 duplicate key ... ix_employees_tenant_id_employee_no`. Derive seed strings from
  `Guid.NewGuid()` (v4, fully random) instead. InMemory never surfaces this.
- **Falsification is mandatory and must be ATOMIC.** Mutate -> build -> run -> revert in ONE Bash call
  with `trap '... git checkout -- $F; sha256sum -c ...' EXIT INT TERM`. A separate revert call can be
  lost to a turn/timeout limit, and an unreverted mutation is indistinguishable from a fix (this
  happened on 2026-09-07; the coordinator had to revert `DepartmentService.cs:317` for me).
  Prefix every scratchpad artifact `i{NNN}-` — generic names collide with parallel agents.

See [[finding-regression-tc-placement]] for where the traceability TC doc goes once the arm lands.
