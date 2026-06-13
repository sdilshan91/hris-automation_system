---
id: TC-LV-111
user_story: US-LV-006
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-111: Clicking a balance card opens the ledger/transaction history (happy path)

## 1. Test Objective
Verify that clicking a leave type balance card opens a detail view that calls `GET /api/v1/leaves/my-ledger?leaveTypeId={id}&year={year}` and renders the transaction history for the current leave year (AC-2, FR-3).

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-2
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated.
- Annual Leave has ledger entries for 2026: monthly accruals, 1 usage, 1 carry-forward from 2025.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Leave type | Annual | Has ledger entries |
| Year | 2026 | Current leave year |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | On the dashboard, click the Annual Leave balance card | `GET /api/v1/leaves/my-ledger?leaveTypeId={annual}&year=2026` is called and returns 200. |
| 2 | Observe the detail view | A ledger/transaction table opens listing each entry with date (occurred_at), transaction type badge, amount, balance-after, and description. |
| 3 | Verify ordering and scope | Entries are scoped to the current leave year (2026) and the selected leave type only; chronological ordering is consistent. |
| 4 | Close the detail view | The view dismisses and returns to the dashboard grid with state intact. |

## 6. Postconditions
- Ledger history is displayed read-only for the selected type and year; no mutation.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
