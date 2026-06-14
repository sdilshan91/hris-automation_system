---
id: TC-ATT-038
user_story: US-ATT-004
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-038: Manager rejects a pending regularization with a mandatory reason -- status REJECTED, no attendance_log change, employee notified with the reason (happy path)

## 1. Test Objective
Verify the rejection flow (AC-2, FR-3, BR-1): when a manager rejects a PENDING regularization for a direct report and supplies a reason (>= 10 characters), the system sets `attendance_regularization.status = REJECTED`, stores the rejection reason in the workflow history, makes NO change to any `attendance_log`, and dispatches a notification to the employee that includes the rejection reason.

## 2. Related Requirements
- User Story: US-ATT-004
- Acceptance Criteria: AC-2
- Functional Requirements: FR-3 (rejection requires reason, stored in workflow history), FR-5 (notification with reason -- seam), FR-6 (audit)
- Business Rule: BR-1 (rejection requires a mandatory reason, min 10 chars)

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, approval workflow configured.
- Manager "Dana Wells" authenticated, holds `Attendance.Approve.Team`.
- Employee "Jordan Lee" reports to Dana.
- A PENDING `attendance_regularization` exists for Jordan (type MISSED_CLOCK_OUT, linked to an existing open `attendance_log`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| regularization_id | Jordan's PENDING request | |
| action | REJECT | |
| comment (reason) | "No supporting evidence of the clock-out time provided." | >= 10 chars (BR-1) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana, `POST /api/v1/attendance/regularizations/{regularization_id}/reject` with body `{ comment: <reason> }` | Response 200; payload shows status REJECTED, actor=Dana, decision timestamp, and the rejection reason. |
| 2 | Re-fetch the regularization | `status = REJECTED`; the rejection reason is persisted in the workflow history; `updated_by` = Dana, `updated_at` set. |
| 3 | Inspect the linked attendance_log | The existing attendance_log is UNCHANGED -- no regularized times applied, total_work_minutes not recalculated (rejection never mutates attendance). |
| 4 | Verify notification (seam) | A notification is dispatched/queued to Jordan, tenant-scoped, payload referencing the regularization_id, the REJECTED outcome, AND the rejection reason text. (CONDITIONAL/DEFERRED on US-NTF -- seam verified now.) |
| 5 | Verify audit | An audit_log entry records action=reject, actor=Dana, timestamp, regularization_id, and the reason. |

## 6. Postconditions
- The regularization is REJECTED with reason stored; no attendance_log mutation occurred; the employee is notified with the reason; the action is audited.

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
- The rejection-reason content propagating to the employee notification is the FR-5/AC-2 "notified with the rejection reason" assertion; the delivery channel is DEFERRED on US-NTF, the reason-in-payload seam is verified now.
