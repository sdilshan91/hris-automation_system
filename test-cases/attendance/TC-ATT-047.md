---
id: TC-ATT-047
user_story: US-ATT-004
module: Attendance
priority: critical
type: integration
status: draft
created: 2026-06-14
---

# TC-ATT-047: Approval atomicity -- a failure mid-approval leaves neither the regularization status nor the attendance_log updated (integration)

## 1. Test Objective
Verify NFR-2: the approve action is atomic across the regularization status update, the attendance_log create/update, the workflow-instance advance, and the audit write. If any step fails (e.g. the attendance_log write or the commit fails after the status flip in the same transaction), the entire transaction rolls back -- the regularization stays PENDING and NO attendance_log is created/modified. There is no half-applied state.

## 2. Related Requirements
- User Story: US-ATT-004
- Non-Functional: NFR-2 (approval/rejection must be atomic -- both update or neither)
- Functional Requirements: FR-2 (attendance_log create/update on approval)

## 3. Preconditions
- Tenant "acme", manager "Dana Wells" authenticated with `Attendance.Approve.Team`.
- A PENDING `attendance_regularization` exists for Dana's direct report Jordan (MISSED_BOTH, no linked log).
- A fault can be injected at the attendance_log persistence/commit boundary (e.g. a forced DB error / unique-violation on the log write) for the test harness.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| regularization_id | Jordan's PENDING request | |
| Injected fault | attendance_log write/commit failure | simulates mid-approval failure |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Arrange a fault at the attendance_log write/commit step. As Dana, `POST .../{regularization_id}/approve`. | Response is a 5xx/handled error; the operation does not partially succeed. |
| 2 | Re-fetch the regularization | Status is still PENDING -- the status flip to APPROVED was rolled back with the failed transaction. |
| 3 | Inspect attendance_log for Jordan on that date | No attendance_log was created (or, for the update branch, the existing log is unchanged). |
| 4 | Inspect workflow_instance | The workflow step was NOT advanced/finalized -- it is consistent with the still-PENDING status. |
| 5 | Remove the fault and retry the approve | Approval now succeeds fully -- status APPROVED, attendance_log written, total recalculated, audit recorded (no leftover/duplicate rows from the failed attempt). |

## 6. Postconditions
- A failed approval leaves the system in its pre-action state (PENDING, no log, no advanced workflow); a clean retry fully applies the approval.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
