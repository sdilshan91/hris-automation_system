---
id: TC-ATT-142
user_story: US-ATT-001
module: Attendance
priority: high
type: integration
status: automated
created: 2026-07-04
defect:
  - ISSUE-067
  - ISSUE-069
  - ISSUE-071
  - ISSUE-073
  - ISSUE-075
  - ISSUE-089
  - ISSUE-093
automated_by:
  - HRM.Tests.Integration.AttendanceAuditWriteTests.ClockIn_WritesAuditRow_ISSUE067
  - HRM.Tests.Integration.AttendanceAuditWriteTests.ClockOut_WritesAuditRow_ISSUE069
  - HRM.Tests.Integration.AttendanceAuditWriteTests.SubmitRegularization_WritesAuditRow_ISSUE071
  - HRM.Tests.Integration.AttendanceAuditWriteTests.ApproveRegularization_WritesApprovedAuditRow_ISSUE073
  - HRM.Tests.Integration.AttendanceAuditWriteTests.RejectRegularization_WritesRejectedAuditRow_ISSUE073
  - HRM.Tests.Integration.AttendanceAuditWriteTests.CreateShift_WritesAuditRow_ISSUE075
  - HRM.Tests.Integration.AttendanceAuditWriteTests.UpdateShift_WritesAuditRowWithBeforeAfter_ISSUE075
  - HRM.Tests.Integration.AttendanceAuditWriteTests.DeleteShift_WritesAuditRow_ISSUE075
  - HRM.Tests.Integration.AttendanceAuditWriteTests.AssignShift_WritesAuditRow_ISSUE075
  - HRM.Tests.Integration.AttendanceAuditWriteTests.CloneShift_WritesAuditRow_ISSUE075
  - HRM.Tests.Integration.AttendanceAuditWriteTests.LockPeriod_WritesAuditRow_ISSUE089
  - HRM.Tests.Integration.AttendanceAuditWriteTests.UnlockPeriod_WritesAuditRow_ISSUE089
  - HRM.Tests.Integration.AttendanceAuditWriteTests.CreateScheduledReport_WritesAuditRow_ISSUE093
  - HRM.Tests.Integration.AttendanceAuditWriteTests.UpdateScheduledReport_WritesAuditRow_ISSUE093
  - HRM.Tests.Integration.AttendanceAuditWriteTests.DeleteScheduledReport_WritesAuditRow_ISSUE093
---

# TC-ATT-142: Attendance write operations append a tenant-scoped, actor-attributed audit_logs row (missing-audit regression cluster)

## 1. Test Objective
Verify that every state-changing Attendance operation appends a structured `audit_logs` row in the same
`SaveChanges` as the mutation — mirroring the reference `LeaveTypeService.AddLeaveTypeAudit` (`AuditLogs.Add`)
pattern. Historically the Attendance services mutated business state but wrote **no** audit trail, so the
per-tenant audit view (US-ADM-008 / technical-doc §19.9) silently missed clock-in/out, regularization
submit/decision, shift lifecycle, payroll-period lock/unlock, and scheduled-report-config changes. This is a
defect-guarding regression cluster covering seven findings at once.

Each audit row must carry the correct standardized **Action** (e.g. `Attendance.ClockedIn`), a **ResourceId**
equal to the mutated entity, the acting **TenantId**, and the acting **UserId** (actor attribution). For
update/lock operations the before/after snapshots must actually differ; for the shared approve/reject code
path the distinct action (`.Approved` vs `.Rejected`) must be recorded.

## 2. Related Requirements
- User Story: US-ATT-001 (clock-in/out), US-ATT-003 (regularization submit), US-ATT-004 (approve/reject),
  US-ATT-005 (shift CRUD/assign/clone), US-ATT-009 (period lock/unlock), US-ATT-010 (scheduled reports)
- Cross-cutting: US-ADM-008 tenant audit-log view / technical-doc §19.9 `audit_log` structured columns
- Defects guarded:
  - **ISSUE-067** — `AttendanceService` clock-in wrote no audit row → `Attendance.ClockedIn`
  - **ISSUE-069** — clock-out wrote no audit row → `Attendance.ClockedOut`
  - **ISSUE-071** — regularization submit wrote no audit row → `AttendanceRegularization.Submitted`
  - **ISSUE-073** — `RegularizationApprovalService` approve/reject wrote no audit row → `.Approved` / `.Rejected`
  - **ISSUE-075** — `ShiftService` create/update/delete/assign/clone wrote no audit row → `Shift.*`
  - **ISSUE-089** — `AttendancePayrollService` period lock/unlock wrote no audit row → `AttendancePeriod.Locked/.Unlocked`
  - **ISSUE-093** — scheduled-report-config create/update/delete wrote no audit row → `ScheduledReport.*`

## 3. Preconditions
- A resolved tenant context (Tenant A) with an authenticated acting user.
- Seed data per operation: an employee linked to the acting user (clock-in/out, submit); a manager +
  direct-report with a pending regularization (approve/reject); a shift + employee (assign); an open
  clock-in log (clock-out).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Acting user (employee) | `_userA` → employee `A1` | drives clock-in/out, submit |
| Acting user (manager) | `_managerUserA` → manager of `REPA` | drives approve/reject |
| Open clock-in log | `ClockIn = now-480m`, no ClockOut | precondition for clock-out |
| Pending regularization | `MissedBoth`, 09:00–17:00, `Pending` | precondition for approve/reject |
| Shift | Single 09:00–17:00, grace 10m | create/update/delete/assign/clone subject |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Clock in as employee A | `audit_logs` row: action ~`ClockedIn`, ResourceId == attendance-log id, TenantId == A, UserId == acting user |
| 2 | Clock out the open log | row: action ~`ClockedOut`, ResourceId == the closed log |
| 3 | Submit a regularization | row: action ~`Submitted`, ResourceId == regularization id |
| 4 | Approve a pending regularization (manager) | row: action ~`Approved`, ResourceId == regularization id, actor == manager |
| 5 | Reject a pending regularization (manager) | row: action ~`Rejected` (distinct from approve on the shared path) |
| 6 | Create a shift | row: action ~`Shift`, ResourceId == shift id |
| 7 | Update the shift (change grace period) | row: action ~`Updated` with non-null Before/After that differ |
| 8 | Delete the shift | row: action ~`Deleted` |
| 9 | Assign the shift to an employee | row: action ~`Assign`, ResourceId == shift id |
| 10 | Clone the shift | row: action ~`Clon`, ResourceId == the NEW cloned shift id |
| 11 | Lock a payroll period | row: action ~`Lock`, ResourceId == period-lock id |
| 12 | Unlock the period | row: action ~`Unlock` (distinct action on the same entity) |
| 13 | Create a scheduled-report config | row: action ~`ScheduledReport`, ResourceId == config id |
| 14 | Update the scheduled-report config | row: action ~`Updated` |
| 15 | Delete the scheduled-report config | row: action ~`Deleted` |

## 6. Postconditions
- One structured `audit_logs` row per operation, tenant-scoped and attributed to the acting user; no audit
  row leaks a different tenant. Pre-fix (services with no `AuditLogs.Add`) every arm fails on row absence
  (`found: <none>`); post-fix all fifteen arms pass.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test (audit trail / accountability)
- [x] Multi-tenant isolation (audit row stamped with acting tenant)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
