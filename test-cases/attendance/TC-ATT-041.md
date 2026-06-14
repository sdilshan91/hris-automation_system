---
id: TC-ATT-041
user_story: US-ATT-004
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-041: Approving a regularization for an employee NOT in the manager's team is denied with the exact authorization message (negative + authz)

## 1. Test Objective
Verify AC-5/FR-7: a manager attempting to approve (or reject) a regularization for an employee who is not in their direct reporting hierarchy is denied, with the EXACT message "You are not authorized to approve requests for this employee." The target request is unchanged (stays PENDING), no attendance_log is touched, and the denied attempt is auditable. Enforcement is server-side, independent of any UI hiding.

## 2. Related Requirements
- User Story: US-ATT-004
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7 (manager may only approve requests for direct reports)
- Preconditions: S2 (`Attendance.Approve.Team` permission)

## 3. Preconditions
- Tenant "acme", manager "Dana Wells" authenticated, holds `Attendance.Approve.Team`.
- "Sam Park" reports to a DIFFERENT manager (Lee Chan), and has a PENDING `attendance_regularization` (id known to the test).
- Dana is NOT in Sam Park's reporting chain.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Acting manager | Dana Wells | holds the permission but not over Sam |
| Target | Sam Park's PENDING regularization_id | out of Dana's team |
| Expected message | "You are not authorized to approve requests for this employee." | exact string |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Dana, `POST /api/v1/attendance/regularizations/{sam_regularization_id}/approve` | Response 403 (authorization failure) with the EXACT message "You are not authorized to approve requests for this employee." |
| 2 | Repeat for the reject endpoint with a valid reason | Same 403 + exact message; the relationship check is enforced for both approve and reject. |
| 3 | Re-fetch Sam Park's regularization | Still PENDING; unchanged; no attendance_log created/updated. |
| 4 | Confirm the gate is server-side | The denial holds even when the request is crafted directly against the API (not via the UI), i.e. it is not merely a hidden button. |
| 5 | Verify audit | The denied authorization attempt is recorded per the tenant's audit policy (actor=Dana, target regularization, outcome=denied). |

## 6. Postconditions
- Out-of-team approval/rejection is refused with the exact message; the target request and attendance remain unchanged.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- A manager entirely lacking `Attendance.Approve.Team` is rejected at the permission gate (403) before the relationship check; an unauthenticated request is 401. Those authn/authz baselines mirror TC-ATT-036's pattern; this TC focuses on the relationship-scoped denial and its exact AC-5 message.
