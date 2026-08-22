---
name: reference-reports-module
description: US-RPT-001 pre-built HR reports — generic HrReportResult envelope, 6 report types, reused permission/cache seams, turnover denominator approximation
metadata:
  type: reference
---

US-RPT-001 (pre-built HR reports) backend lives in `Features/Reports` + `Infrastructure/Services/HrReportService.cs`.
Read-only aggregation over Employee / Department / Location / EmploymentHistory — NO new entity/migration.

Key reuse + decisions worth recalling before touching the Reports module:
- **Permission = `Reports.View`** (already in PermissionCatalog; held by Manager/HROfficer/HRManager/Auditor/
  TenantAdmin). Do NOT invent new permission constants. Export will be `Reports.Export` (US-RPT-004).
- **Generic envelope `HrReportResult`** serves all 6 report types: ReportType, Title, GeneratedAt,
  FiltersApplied (dict), `Charts` (list of `HrChartSeries{Name,ChartType,Points[]}`), Columns, Rows
  (string cells), optional TotalRow, FromCache flag. Reuse this shape for new report types.
- **6 report-type IDs** (HrReportType enum, case-insensitive path/body value): HeadcountSummary,
  EmployeeTurnover, Demographics, JoinersAndLeavers, DepartmentDistribution, EmploymentTypeBreakdown.
- **Endpoints** (HrReportsController, route `api/v1/reports/hr`): GET / (catalog of 6 descriptors),
  GET /{reportType} (filters via querystring), POST /{reportType} (filters in body — preferred for the
  multi-value arrays). `refresh=true` bypasses cache (FR-8).
- **Caching**: optional `IDistributedCache` (same Redis-or-in-memory seam as TenantSettings/NotificationPrefs),
  key `t:{tenantId}:report:{name}:{paramsHash}` (SHA256[..16] of normalized filters), 10-min TTL.
- **BR rules are static helpers on HrReportService (unit-tested)**: `IsActive` (Active|Probation, BR-4),
  `IsSeparatedValue` (Terminated/Inactive enum OR free-text resign/terminat/contract-ended, BR-4),
  `IsVoluntary` (resign=voluntary, terminat=involuntary, else involuntary, BR-3), `AgeAt(dob, reportDate)`
  (BR-5 age at report date, not now).
- **Separations come from EmploymentHistory** rows where `ChangeType=="Status"` and `EffectiveDate` in window
  (Employee has NO TerminationDate column). **Turnover denominator** = current-active + in-window separations
  (no point-in-time snapshot table exists) → documented approximation; true avg-headcount snapshot/materialized
  table is the FR-6 follow-up.
- **InMemory-safety applied** (see [[feedback-inmemory-required-nav-projection]]): department/location NAMES
  resolved via separate `ToDictionary` lookups, never projected through the filtered nav; `List.Contains` (not
  HashSet) for IN filters; `.ToListAsync()` then in-memory LINQ for grouping/age math.
- Integration tests mirror PayrollReport idiom (MutableTenantContext + InMemory, [[feedback-integration-tests-inmemory]]).
  The 100-emp/terminate-10 → 10% turnover hint passes exactly with the denominator above.
- **BR-2 role scope (ISSUE-195, DONE)**: all 6 HR builders now scope their base employee population via
  `ResolveScopeAsync`→`ApplyScopeToEmployees` (the SAME resolver US-RPT-002 leave/attendance use). Scope kind
  keyed on cross-module `.View.All` (Employee/Leave/Attendance) → "All"; else if the caller's Employee has ≥1
  direct report (`ReportsToEmployeeId == me.Id`) → "Manager" (direct reports + self); else "Employee". Each HR
  builder emits a `"Scope"` summary stat and is now in `IsScopedReport` so the cache key folds scope (a Manager
  and HR never share an entry). NO new permission was invented — `Reports.View.Team` still does not exist; the
  Manager-vs-All decision is has-direct-reports + lacks-.View.All, matching the leave/attendance path exactly.
- **BUG-120 == ISSUE-018 (duplicate, already fixed #147)**: `EmployeesController.GetDirectory` widened to
  `[RequirePermission(Own,Team,All)]` (OR-semantics); covered by `EmployeeDirectoryAuthorizationTests.cs`
  (reflection-driven, tracks the controller attribute). Don't re-add a duplicate test.
- **Deferred**: export (US-RPT-004 — done since), materialized views (FR-6).
