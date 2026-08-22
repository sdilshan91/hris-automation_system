---
name: reference-attendance-module
description: Where attendance domain rules live and key US-ATT-001 scaffold decisions
metadata:
  type: reference
---

Attendance module domain rules, the enforcement/error-code contract, and deviations from the
story's aspirational tech (no RLS, no Redis) are documented in the shared vault at
`docs/vault/modules/attendance.md`. Read it before working any `US-ATT-*` story.

US-ATT-002 (clock-out) added: pure `AttendanceCalculator.Calculate(...)` in
`HRM.Domain/Entities/AttendanceCalculator.cs` (work-hours/overtime/status, shared by the clock-out
service path AND the `AutoClockOutJob` BR-5 recurring job); calc policy fields on `AttendanceSettings`
(tenant-level fallback, move to shift entity at US-ATT-005); `ClockStatusDto.LastCompleted` summary.

US-ATT-003 (regularization) added: `AttendanceRegularization` entity (PENDING on submit, never
mutates the log — that's US-ATT-004), placeholder `PayrollLockPeriod` for AC-5 (Payroll module will
own it later), `AttendanceSettings.RegularizationLookbackDays` (default 7). Permission: ADDED the
literal `Attendance.Regularize.Self` to PermissionCatalog + Employee role seed (contrast ATT-001/002
which reused `Attendance.CheckIn`). FR-3 workflow engine + FR-4 notifications DEFERRED (TODO
US-ADM-007 / US-NTF). All exact AC reject messages + the API contract are in the vault note.

US-ATT-004 (manager approve/reject) added: dedicated `IRegularizationApprovalService` /
`RegularizationApprovalService` (NOT folded into `AttendanceService` — kept separate like
LeaveReportService/LopService). Immutable decision record entity `RegularizationApprovalHistory`
(mirrors `LeaveApprovalHistory`; table `attendance_regularization_history`; insert-only = NFR-4).
APPROVE is single-level FINAL: creates/updates the `attendance_log` for employee+date via the SAME
`AttendanceCalculator`, single atomic SaveChanges. Auth = DIRECT REPORTS ONLY
(`ReportsToEmployeeId == manager.Id`), Phase-1 limit (full hierarchy deferred). EXACT denial msg:
"You are not authorized to approve requests for this employee." BR-6 self-approval blocked
(`self_approval`, 403, checked before team-check). BR-3 immutability → `already_actioned` 409. BR-5
payroll lock RE-checked at approval. BR-7 bulk = per-item loop, each its own SaveChanges, partial
results. Permission: ADDED literal `Attendance.Approve.Team` to catalog + Manager/HROfficer/HRManager/
TenantAdmin role seeds. **AC-4 multi-level CANNOT be satisfied** (no workflow engine, US-ADM-007) —
flagged, workflow_instance_id stays null. FR-5 notif / FR-8 Redis deferred. Migration
`20260614161602_Attendance_RegularizationApproval`. Mirror the Leave approve pattern
(`LeaveRequestService.ApproveAsync`/`LoadForDecisionAsync`) for any future approval story.

US-ATT-005 (shift mgmt + assignment) added: `Shift`/`ShiftRotationStep`/`EmployeeShift` entities,
`IShiftService`/`ShiftService`, all endpoints on `AttendanceController` under `/api/v1/attendance/shifts`
gated by NEW concrete perm `Attendance.Shift.Manage` (constant `Attendance.ManageShift`; story's
`Attendance.*.All` wildcard isn't a catalog entry). Granted TenantAdmin/HRManager/HROfficer.
**DbInitializer gained a real per-tenant reconcile pass** (`ReconcileAllTenantsAsync`): adds missing
built-in role perms (add-only — the pre-ATT-005 initializer did NOT reconcile perms despite the ATT-004
comment claiming so) AND seeds an idempotent default shift ("General Shift", Mon–Fri 09:00–17:00,
`IsDefault=true`) for every tenant (BR-1/FR-5). Effective-dating closes the prior open assignment at
`effectiveFrom-1` (BR-2, no overlap). Delete blocked when assigned with EXACT msg + code `shift_in_use`
409. Rotation = real child table (queryable, not jsonb); resolve via day-index mod cycle. Night shift
(end<start) allowed; start==end rejected (BR-7). Clock-out wiring to shift policy DEFERRED (shift lacks
the calculator's Standard/AutoBreakThreshold/Overtime fields — TODO in AttendanceCalculator). No Redis
(NFR-4), no RLS (NFR-3). Migration `20260614164322_Attendance_Shifts`.

US-ATT-007 (monthly summary) added: `AttendanceMonthlySummary` entity (materialized cache — Redis
FR-8 DEFERRED, the table IS the cache), `IAttendanceSummaryService`/`AttendanceSummaryService` (aggregates
AttendanceLog + APPROVED OvertimeRecord + approved AttendanceRegularization + resolved Shift/EmployeeShift
+ Holiday + approved LeaveRequest). Endpoints on `AttendanceController` under `/api/v1/attendance/summary/monthly*`
gated by EXISTING `Attendance.View.All` (story said `Attendance.Read.All` — not a catalog entry; reused
View.All like the ATT-006 overtime report). Half-day (BR-5): NEW `AttendanceSettings.HalfDayEnabled` (default
OFF); half-day band = [50% standard, standard). Late/early (FR-3): computed HERE from shift start+grace /
end (US-ATT-008 not built — self-contained, may be refined later). Jobs: `MonthlySummaryDailyJob` (01:00 UTC,
refreshes prev-day's month) + `MonthlySummaryMonthlyJob` (01:30 1st, finalizes prev month) — both per-tenant
via `ITenantContext.SetTenant` like AutoClockOutJob. Large export (>1000 emp) → `AttendanceSummaryExportJob`
Hangfire + IReportExportStorage; FR-7 NOTIFICATION DEFERRED (US-NTF). **PDF: ADDED QuestPDF** (Community
license) to HRM.Infrastructure.csproj — LeaveReports only did CSV/XLSX. Migration
`20260614173721_Attendance_MonthlySummary`. MonthBounds caps the current incomplete month at today UTC (AC-3).

US-ATT-010 (HR dashboard + reports — FINAL attendance story) added: `ScheduledReportConfig` entity
(BaseEntity; jsonb filters, uuid[] recipients, time delivery) + `IAttendanceDashboardService`/
`AttendanceDashboardService`. NO new tables for KPIs/live-board/trends — they aggregate existing data;
trends + dept-comparison read `AttendanceMonthlySummary` (BR-5) and REUSE `IAttendanceSummaryService`.
Endpoints on `AttendanceController` under `/api/v1/attendance/dashboard*` and `/reports/*`, all gated by
EXISTING `Attendance.View.All`; team scope (BR-4) narrows to direct reports (`ReportsToEmployeeId`) and
is enforced IN THE SERVICE (scope=all requires View.All in `_currentUser.Permissions`, else 403
`scope_not_allowed`). Live-board status precedence: CLOCKED_IN > ON_LEAVE (full-day approved) > HOLIDAY
(at employee location/tenant-wide) > NOT_CLOCKED_IN. KPI expected = active − full-day-leave − holiday
(BR-1); absent == pending (not-in & not-leave/holiday). Custom report present = DISTINCT clock-in days;
absent = scheduled working days − present − leave. Export reuses ATT-007 CSV/XLSX(ClosedXML)/PDF(QuestPDF).
**DEFERRALS (do NOT build): SignalR live push (FR-2/NFR-2) — live board is a plain GET the FE POLLS (§10);
Redis KPI cache (FR-7/NFR-1) — computed from DB; scheduled-report EMAIL delivery (FR-8) — `ScheduledReportJob`
Hangfire recurring (hourly `0 * * * *`, per-tenant, de-dupes by frequency+LastRunAt) GENERATES + stores the
file but the send is LOGGED/no-op (TODO US-NTF); no RLS (NFR-4).** Migration `20260614192122_Attendance_DashboardReports`
(only creates `scheduled_report_config` — no drift). Pinned API contract honored verbatim (FE+QA built against it).

ISSUE-065 / Phase 2b (tenant-timezone-aware attendance) added: shared pure helper
`HRM.Application/Common/Helpers/TenantClock.cs` — `ResolveTimeZone(tzId, logger?)` (IANA via ICU on
.NET 10, UTC fallback on TimeZoneNotFound/InvalidTimeZone, never throws), `LocalDateOf`/`LocalTimeOfDay`
(UTC instant → tenant-local day / time-of-day), `TodayIn(tz)`, `LocalToUtc(date,time,tz)`. All conversions
are an EXACT no-op when tz==UTC (the default `Tenant.TimeZone`, and what the whole attendance test-suite
uses) — that's what keeps existing tests green. Each service resolves the tenant zone ONCE per op via a
private `ResolveTenantTimeZoneAsync`/`ResolveTenantZoneAsync` (reads `Tenant.TimeZone`) and threads the
`TimeZoneInfo` down. Converted: `AttendanceService` clock-in/out lock-date + shift-resolution date +
late/early comparison, regularization future-date/lookback "today" + existing-log day-match window +
`RequestedClockIn/Out` wall-clock combine; `AttendanceSummaryService` month/day grouping + query window +
prior-quarter window + MonthBounds/shift-filter "today". Summary late/early COUNTS read the persisted
`is_late`/`is_early` flags (already tz-correct from clock-in/out) — not recomputed. `RegularizationDtos`
gained `CombineToUtc(value, TimeZoneInfo)`; the parameterless overload (== tz=UTC) stays for the shape
VALIDATOR (tz-invariant ordering + coarse future check; validator has no DI/tz access). STILL UTC (flagged,
NOT fixed this pass): `AttendanceDashboardService` + `AttendancePayrollService` day-grouping — same defect
class, follow-up. No migration (column existed).

CAL-4a/4b (US-ATT-011 AC-3) — ⚠ THE TRAP: `AttendanceSettings` is NO LONGER "one row per tenant". It is
one row per (tenant, location): `LocationId == null` = tenant default, set = that Location's override.
CAL-4a repointed SEVEN pre-existing unpredicated `FirstOrDefaultAsync()` reads that silently relied on the
old invariant — an unpredicated read now returns an ARBITRARY row and applies one branch's geofence/OT
policy tenant-wide. **Every AttendanceSettings read/write must be predicated on `LocationId`** — via
`AttendancePolicyResolver` (`internal static`, HRM.Infrastructure.Services: `ResolveForEmployeeAsync` /
`GetOrCreateTenantDefaultAsync` / `GetTenantDefaultOrNullAsync`) when an employee is in scope, else an
explicit `LocationId == null` / `== locationId` filter. Resolution is ROW-LEVEL (an override IS the
complete policy — never merge per-field; creating one does NOT copy the tenant row). CAL-4b added the
admin CRUD (`IAttendanceSettingsService`, 6 routes under `/api/v1/attendance/settings*`, gated by the
EXISTING `Attendance.ConfigurePolicy`). Two unique indexes are the contract (`..._tenant_location_unique`
+ the PARTIAL `..._tenant_default_unique`, which exists because Postgres treats NULLs as distinct) → these
suites MUST be Postgres/Testcontainers, not InMemory. Testing gotcha: an invariant arm only fails against
an unpredicated read if the OVERRIDE rows are seeded FIRST — default-first ordering passes by luck.

Key scaffold facts from US-ATT-001:
- Entities `AttendanceLog` + `AttendanceSettings` (both `BaseEntity`), one settings row per tenant
  created lazily with enforcement off.
- "One OPEN punch per employee" (clock_out IS NULL) is the duplicate rule, backed by a partial
  unique index `ix_attendance_log_open_unique` — NOT a calendar-day rule.
- Clock-in is gated by the existing `Attendance.CheckIn` permission (the story's
  `Attendance.Clock.Self` does not exist in `PermissionCatalog`).
- Related: [[feedback-integration-tests-inmemory]].
