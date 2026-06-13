---
id: TC-LV-106
user_story: US-LV-005
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-106: A request cancelled by the employee cannot be approved or rejected

## 1. Test Objective
Verify the precondition that a request cancelled by the employee is no longer actionable: attempting to approve or reject a cancelled request returns an error, the request stays Cancelled, and no ledger/history entry is created.

## 2. Related Requirements
- User Story: US-LV-005
- Preconditions (Section 2): the leave request has not been cancelled by the employee
- Business Rules: BR-3 (only an actionable Pending request can be decided)

## 3. Preconditions
- Tenant "acme" is active; Manager "Robert Lee" authenticated with `Leave.Approve.Team`.
- Direct report "Jane Smith" submitted a request and then cancelled it; its `status = Cancelled`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request | Jane Smith, Annual Leave | status Cancelled |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Robert, `POST /api/v1/leaves/{id}/approve` on the cancelled request | API returns an error (e.g., 409/422): the request is not in an actionable state. |
| 2 | As Robert, `POST /api/v1/leaves/{id}/reject` with a reason | Same error; no state change. |
| 3 | Inspect the request | `status` remains `Cancelled`; no `used` ledger entry; no `leave_approval_history` row. |
| 4 | UI: the cancelled request should not appear in the pending queue | It is absent from the manager's pending list (only Pending requests are actionable). |

## 6. Postconditions
- Cancelled request stays Cancelled; no side effects from the action attempts.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
