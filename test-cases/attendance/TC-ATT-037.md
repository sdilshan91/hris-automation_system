---
id: TC-ATT-037
user_story: US-ATT-004
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-037: Manager approves a pending regularization -- status APPROVED, attendance_log created/updated with regularized times, employee notified (happy path)

## 1. Test Objective
Verify the primary approval flow (AC-1, FR-2): when a manager approves a PENDING regularization for a direct report on a single-level workflow, the system (a) sets `attendance_regularization.status = APPROVED`, (b) creates or updates the corresponding `attendance_log` with the regularized clock-in/clock-out times, (c) recalculates `total_work_minutes` from those times, (d) records the optional approval comment, manager id, and timestamp in the workflow history, and (e) dispatches an employee notification. The whole effect is committed atomically (see TC-ATT-047).

## 2. Related Requirements
- User Story: US-ATT-004
- Acceptance Criteria: AC-1
- Functional Requirements: FR-2 (create/update attendance_log, recalc total_work_minutes), FR-6 (audit), FR-5 (notification -- seam, see Notes)
- Non-Functional: NFR-2 (atomic -- cross-ref TC-ATT-047)
- Business Rule: BR-2 (approval comment optional), BR-4 (log updated only on final approval -- single-level here = final)

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, single-level approval workflow configured (the manager is the only/final approver).
- Manager "Dana Wells" authenticated, holds `Attendance.Approve.Team`.
- Employee "Jordan Lee" reports to Dana Wells (`manager_id` = Dana).
- A PENDING `attendance_regularization` exists for Jordan for `date = today - 2 days`, type MISSED_BOTH, requested clock-in 09:00 and clock-out 17:30 (tenant-local), reason >= 10 chars, `attendance_log_id` null. The target date is NOT in a locked payroll period.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| regularization_id | Jordan's PENDING request | The request to act on |
| action | APPROVE | |
| comment | "Verified with team roster." | Optional for approval (BR-2) |
| requested clock_in / clock_out | 09:00 / 17:30 tenant-local | Stored UTC; total = 8h30m minus tenant break policy |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana Wells, `POST /api/v1/attendance/regularizations/{regularization_id}/approve` with body `{ comment }` | Response 200; payload shows status APPROVED, the decision actor (Dana), a UTC decision timestamp, and the comment. |
| 2 | Re-fetch the regularization | `status = APPROVED`; `updated_by` = Dana; `updated_at` set; workflow history holds the approval step with Dana's id, timestamp, and comment. |
| 3 | Inspect attendance_log for Jordan on that date | An `attendance_log` exists (created since none was linked) with `clock_in`/`clock_out` = the regularized times (UTC), tenant_id = acme, employee_id = Jordan. |
| 4 | Verify total_work_minutes | `total_work_minutes` is recalculated from the regularized clock_in/clock_out per the tenant break policy (e.g. 510 min span minus configured break), accurate to the minute. |
| 5 | Verify notification dispatch (seam) | An employee-directed notification is dispatched/queued to Jordan, tenant-scoped, payload referencing the regularization_id and the APPROVED outcome. (CONDITIONAL/DEFERRED on the Notification System -- see Notes; verify the dispatch seam now.) |
| 6 | Verify audit | An audit_log entry records action=approve, actor=Dana, timestamp, regularization_id, and comment, tenant-scoped (see TC-ATT-048). |

## 6. Postconditions
- The regularization is APPROVED; the attendance_log reflects the regularized times with a recalculated total; employee notified; all changes tenant-scoped and audited.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Notification (FR-5):** the Notification System (US-NTF) is not built. Step 5 verifies the dispatch SEAM (correct recipient = the requesting employee, tenant-scoped, payload references regularization_id + outcome) now and DEFERS end-to-end in-app delivery/badge assertions until US-NTF lands. Consistent with US-ATT-003 TC-ATT-032.
- **attendance_log create vs update:** this TC covers the CREATE branch (MISSED_BOTH, no prior log). The UPDATE branch (MISSED_CLOCK_OUT linked to an existing open log) is the same mechanism; total_work_minutes recalculation is asserted in both. Single-level workflow here makes the manager the final approver, so the log is written at this approval (BR-4); the multi-level deferral is in TC-ATT-044.
