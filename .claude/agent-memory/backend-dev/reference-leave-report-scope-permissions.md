---
name: leave-report-scope-permissions
description: US-LV-012 BR-2 row scope keys on cross-module Reports.View.* (DEC-1), not Leave.Reports.* — plus the two traps that make a scoped-report test pass vacuously
metadata:
  type: reference
---

# Leave report scoping (US-LV-012 BR-2, ENH-002)

BR-2 verbatim: *"Employee-level data in reports respects role-based access: HR sees all; managers see
their team; employees see only their own data."* Implemented as `ReportScope` in
`HRM.Infrastructure/Services/LeaveReportService.cs` — `ResolveScopeAsync` picks the bucket,
`ScopedEmployeesQuery` applies it (Manager = `ReportsToEmployeeId == me.Id || Id == me.Id`; Employee =
`Id == me.Id`; unresolved employee record = `Where(e => false)`, i.e. fail-closed to nothing).

## The gotcha: the ENDPOINT gate and the ROW scope read DIFFERENT permissions

- The `[RequirePermission]` gate on `LeaveReportsController` reads `Leave.Reports*`.
- `ResolveScopeAsync` reads the **cross-module** `Reports.View.All` / `Reports.View.Team` (the DEC-1
  dedicated report row-scope perms), *not* `Leave.Reports`.

So the two halves can disagree silently. ENH-002 was exactly that: the gate demanded `Leave.Reports`
(admin-tier only), so Manager/Employee 403'd before the scope branches could ever run — implemented,
tested-at-unit-level, and completely unreachable over HTTP. The fix added `Leave.Reports.Team` /
`Leave.Reports.Own`, seeded them on the built-in Manager/Employee roles, broadened the gate to OR over
all three, and taught `ResolveScopeAsync` to accept `Leave.Reports.Team` for the team bucket too.

`HrReportService` has the same DEC-1 resolver shape — assume the same split applies there.

## Two ways a scoped-report test passes for the wrong reason

1. **The built-in Manager role also holds `Reports.View.Team`.** A persona built from
   `PermissionCatalog.DefaultPermissionsFor(Manager)` therefore resolves to team scope *whatever the
   `Leave.Reports.Team` resolver branch does*. To prove that branch you need a **custom role granting
   exactly one permission** and nothing else. Same trap will apply to any future `X.Reports.Team`.
2. **`BuildBalanceSummaryAsync` skips any (employee × leave type) pair with zero entitlement AND zero
   ledger activity.** Under the real `ILeaveEntitlementService` with no seeded entitlement rules that is
   every pair — the report comes back **empty**, and "manager does not see the other team's rows" then
   passes vacuously. Seed a `LedgerEntryType.Used` row (negative `Amount`) per employee for the report
   year so every employee is genuinely eligible to appear.

## Naming: `.Own`, not `.Self`

Row-scope READ permissions in `PermissionCatalog` are `.Own` / `.Team` / `.All` (9 occurrences across
Employee/Leave/Attendance/Payroll/Performance/Reports/Training/Benefits). `.Self` is used only for
ACTION permissions — `Attendance.Regularize.Self`, `Performance.Read.Self`. New scoped read → `.Own`.

Related: [[reference-reports-module]], [[feedback-guards-must-be-mutation-proven]],
[[reference-seeder-assertions-via-apitestfactory]]
