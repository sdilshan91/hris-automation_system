---
id: TC-LV-211
user_story: US-LV-011
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-211: Declining the LOP prompt creates NO leave request (negative path)

## 1. Test Objective
Verify that when an employee is shown the zero-balance LOP prompt and declines (cancels), no `leave_request` is created, no LOP entry is persisted, and no balance is changed (AC-1, FR-4).

## 2. Related Requirements
- User Story: US-LV-011
- Acceptance Criteria: AC-1
- Functional Requirements: FR-4

## 3. Preconditions
- Tenant "acme"; LOP type exists.
- Employee "Jane Smith" has 0 balance for Annual Leave (no negative allowed), authenticated with `Leave.Apply`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Selected type | Annual Leave | balance = 0 |
| Confirmation | No (Cancel) | declines LOP |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Jane submits a 2-day Annual Leave request (balance 0) | The "...processed as Loss of Pay (LOP)." prompt appears. |
| 2 | Jane declines / cancels the prompt | No request is submitted; the create call is not executed (or is aborted with no persistence). |
| 3 | Query `leave_request` for Jane | No new row (neither LOP nor Annual) was created for those dates. |
| 4 | Query `leave_ledger` for Jane | No new entry; balances unchanged. |

## 6. Postconditions
- No state change occurred; declining the LOP prompt is a clean no-op.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
