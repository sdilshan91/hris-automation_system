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

Key scaffold facts from US-ATT-001:
- Entities `AttendanceLog` + `AttendanceSettings` (both `BaseEntity`), one settings row per tenant
  created lazily with enforcement off.
- "One OPEN punch per employee" (clock_out IS NULL) is the duplicate rule, backed by a partial
  unique index `ix_attendance_log_open_unique` — NOT a calendar-day rule.
- Clock-in is gated by the existing `Attendance.CheckIn` permission (the story's
  `Attendance.Clock.Self` does not exist in `PermissionCatalog`).
- Related: [[feedback-integration-tests-inmemory]].
