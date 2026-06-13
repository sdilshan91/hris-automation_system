---
id: TC-LV-100
user_story: US-LV-005
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-100: A user without Leave.Approve.Team is denied approve/reject

## 1. Test Objective
Verify that the approve and reject endpoints are gated by the `Leave.Approve.Team` permission: a user lacking that permission (e.g., a plain Employee, or an HR Officer without the team-approval grant) is denied with 403 Forbidden and cannot action any request.

## 2. Related Requirements
- User Story: US-LV-005
- Preconditions (Section 2): Manager must hold `Leave.Approve.Team`
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" is active.
- User "Tom Reed" is an authenticated Employee WITHOUT `Leave.Approve.Team`.
- A pending request R from some employee exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | Tom Reed | No Leave.Approve.Team |
| Request R | Pending | Target |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Tom, `POST /api/v1/leaves/{R}/approve` | 403 Forbidden -- permission denied. |
| 2 | As Tom, `POST /api/v1/leaves/{R}/reject` with a reason | 403 Forbidden -- permission denied. |
| 3 | Inspect R | `status` remains `Pending`; no ledger/history/audit entries written. |
| 4 | UI: as Tom, navigate to a request detail (if reachable) | Approve/Reject controls are not rendered for a user without the permission. |

## 6. Postconditions
- Tom's attempts are denied; R unchanged.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
