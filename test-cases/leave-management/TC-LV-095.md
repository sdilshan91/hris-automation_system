---
id: TC-LV-095
user_story: US-LV-005
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-095: An already-actioned request (Rejected/Approved) cannot be re-approved or re-rejected

## 1. Test Objective
Verify that a request that has already been actioned cannot be transitioned again: a rejected request cannot be re-approved (the employee must submit a new request), and an approved request cannot be approved or rejected again. The API returns an already-actioned error and no second ledger/history entry is created (BR-3).

## 2. Related Requirements
- User Story: US-LV-005
- Business Rules: BR-3
- Functional Requirements: FR-1, FR-2

## 3. Preconditions
- Tenant "acme" is active; Manager "Robert Lee" authenticated with `Leave.Approve.Team`.
- Request R1 from a direct report is already `Rejected` (actioned in TC-LV-090 style).
- Request R2 from a direct report is already `Approved` with a `used` ledger entry.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| R1 | status Rejected | Cannot be re-approved |
| R2 | status Approved | Cannot be re-actioned |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST /api/v1/leaves/{R1}/approve` | API returns an error (e.g., 409/422) stating the request has already been actioned; R1 stays `Rejected` (BR-3). |
| 2 | Query `leave_ledger` for R1 | No `used` entry is created (rejection never created one and re-approve is blocked). |
| 3 | `POST /api/v1/leaves/{R2}/approve` again | Error: request already actioned; no second `used` ledger entry; R2 stays `Approved`. |
| 4 | `POST /api/v1/leaves/{R2}/reject` | Error: request already actioned (cannot reject an approved request); R2 stays `Approved`. |
| 5 | UI: open an already-actioned request | The Approve/Reject buttons are hidden/disabled; only the decision outcome is shown. |

## 6. Postconditions
- R1 stays Rejected, R2 stays Approved; no additional ledger/history entries created.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
