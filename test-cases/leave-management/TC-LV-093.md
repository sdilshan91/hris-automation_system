---
id: TC-LV-093
user_story: US-LV-005
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-093: Approval with insufficient balance prompts a confirmation when negative balance IS allowed; confirming creates a negative-balance ledger entry

## 1. Test Objective
Verify that when the balance is insufficient at approval time but the leave type allows a negative balance (within its configured limit), the manager is warned and asked to confirm; on confirmation the request is approved and a `used` ledger entry is created taking the balance negative within the allowed limit (AC-3, BR-5).

## 2. Related Requirements
- User Story: US-LV-005
- Acceptance Criteria: AC-3
- Business Rules: BR-5
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" is active; Manager "Robert Lee" authenticated with `Leave.Approve.Team`.
- Leave type "Unpaid Leave" has `negative_balance_allowed = true`, `negative_balance_limit = 30.00`.
- Direct report "Priya Nair" has a pending 5-day Unpaid Leave request; her current Unpaid balance is 2.00 days.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Leave type | Unpaid Leave | negative_balance_allowed = true, limit 30 |
| Request days | 5.00 | Exceeds current 2.00 balance |
| Priya balance now | 2.00 days | Would go to -3.00 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Robert, click "Approve" on Priya's 5-day request | The system detects the balance (2.00) is below the requested 5.00 and the leave type allows negative balance. |
| 2 | Observe the UI | A confirmation modal warns about the resulting negative balance and asks the manager to confirm (Section 8). |
| 3 | Confirm the approval | API returns 200; `status = Approved`. |
| 4 | Query `leave_ledger` | A `used` entry with `days = 5.00` and `balance_after = -3.00` is created (within the -30 limit). |
| 5 | Boundary: attempt an approval that would exceed the negative limit (e.g., remaining capacity smaller than requested) | Approval is blocked with a limit-exceeded error; no ledger entry created. |

## 6. Postconditions
- On confirm: Priya's request Approved; balance -3.00 days (within limit).
- Beyond-limit attempt blocked; no ledger entry.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
