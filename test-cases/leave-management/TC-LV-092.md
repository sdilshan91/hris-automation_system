---
id: TC-LV-092
user_story: US-LV-005
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-092: Approval blocked when balance became insufficient since submission and negative balance is NOT allowed

## 1. Test Objective
Verify that when the employee's balance has dropped below the requested days since submission (e.g., an earlier request was approved in the interim) and the leave type does NOT allow a negative balance, the manager's approval is blocked with an insufficient-balance error and no ledger entry is created (AC-3, BR-5: balance checked at approval time, not request time).

## 2. Related Requirements
- User Story: US-LV-005
- Acceptance Criteria: AC-3
- Business Rules: BR-5
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" is active; Manager "Robert Lee" authenticated with `Leave.Approve.Team`.
- Leave type "Annual Leave" has `negative_balance_allowed = false`.
- Direct report "Jane Smith" submitted a 3-day Annual Leave request when she had 4.00 days.
- Since submission, another of Jane's requests was approved, dropping her current Annual balance to 2.00 days.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Leave type | Annual Leave | negative_balance_allowed = false |
| Request days | 3.00 | Exceeds current balance |
| Jane balance now | 2.00 days | After interim approval |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Robert, click "Approve" on Jane's 3-day request | The balance is re-checked at approval time against the current 2.00 days (BR-5). |
| 2 | Observe the result | Approval is BLOCKED; API returns a 4xx with an insufficient-balance error message; the manager is informed the balance is insufficient. |
| 3 | Inspect the request status | `status` remains `Pending` (not Approved). |
| 4 | Query `leave_ledger` | No `used` entry is created; Jane's balance remains 2.00 days. |
| 5 | Confirm no audit "Approved" record | No `Leave.Approved` audit entry is written for this attempt. |

## 6. Postconditions
- Request stays Pending; no ledger entry; balance unchanged.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
