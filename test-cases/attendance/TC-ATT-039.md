---
id: TC-ATT-039
user_story: US-ATT-004
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-039: Rejection without a reason (or reason < 10 characters) is rejected by the system; request stays PENDING (negative + boundary)

## 1. Test Objective
Verify BR-1/FR-3: a REJECT action with a missing/empty reason, or a reason shorter than the 10-character minimum, is refused with a validation error; the regularization remains PENDING and unchanged. Confirm the boundary: exactly 10 characters is accepted, 9 is rejected. Approval comments stay optional (BR-2 control).

## 2. Related Requirements
- User Story: US-ATT-004
- Functional Requirements: FR-3 (reason required, min 10 chars, stored in workflow history)
- Business Rules: BR-1 (rejection reason mandatory, min 10 chars), BR-2 (approval comment optional -- positive control)

## 3. Preconditions
- Tenant "acme", manager "Dana Wells" authenticated with `Attendance.Approve.Team`.
- A PENDING `attendance_regularization` exists for Dana's direct report Jordan Lee.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| action | REJECT | |
| comment (empty) | "" / omitted | Must be rejected |
| comment (9 chars) | "Too short" (9) | Below minimum -- rejected |
| comment (10 chars) | "Valid one." (10) | At minimum -- accepted |
| approval comment | omitted | Approval must succeed without a comment (BR-2) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Reject with `comment` omitted/empty | Response 400 (validation error) stating a reason is required; regularization stays PENDING. |
| 2 | Reject with a 9-character reason | Response 400 (below 10-char minimum); regularization stays PENDING. |
| 3 | Re-fetch the regularization after steps 1-2 | Still PENDING; no workflow-history rejection entry; no attendance_log change; no notification dispatched. |
| 4 | Reject with an exactly-10-character reason | Response 200; status REJECTED; reason persisted (boundary accepted). |
| 5 | (Positive control, fresh PENDING request) Approve with NO comment | Response 200; status APPROVED -- approval comment is optional per BR-2. |

## 6. Postconditions
- Below-minimum/empty rejection reasons are refused and leave the request PENDING; a 10-char reason is accepted; approval needs no comment.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
