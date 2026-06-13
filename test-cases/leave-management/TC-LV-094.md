---
id: TC-LV-094
user_story: US-LV-005
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-094: Rejection with an empty/missing reason is rejected with a validation error

## 1. Test Objective
Verify that the reject action enforces a mandatory rejection reason: submitting reject with an empty, whitespace-only, or missing `reason` returns a validation error and the request stays Pending (BR-2, FR-2).

## 2. Related Requirements
- User Story: US-LV-005
- Business Rules: BR-2
- Functional Requirements: FR-2

## 3. Preconditions
- Tenant "acme" is active; Manager "Robert Lee" authenticated with `Leave.Approve.Team`.
- A pending leave request from a direct report exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| reason (empty) | "" | Should be rejected |
| reason (whitespace) | "   " | Should be rejected (trim) |
| reason (missing) | _omitted_ | Should be rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST /api/v1/leaves/{id}/reject` with `reason = ""` | 400 validation error: the rejection reason is required. |
| 2 | Repeat with `reason = "   "` (whitespace only) | 400 validation error (reason is trimmed and treated as empty). |
| 3 | Repeat with the `reason` field omitted entirely | 400 validation error. |
| 4 | Inspect the request status after each attempt | `status` remains `Pending`; no `leave_approval_history` row and no audit `Leave.Rejected` entry are written. |
| 5 | UI: open the reject panel and attempt to submit with an empty textarea | The submit button is disabled or an inline "Reason is required" error is shown; no request is sent. |

## 6. Postconditions
- Request remains Pending; no history or audit record created for the invalid attempts.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
