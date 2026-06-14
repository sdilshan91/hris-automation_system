---
id: TC-LV-149
user_story: US-LV-008
module: Leave Management
priority: critical
type: integration
status: draft
created: 2026-06-14
---

# TC-LV-149: Year-end carry-forward job -- 8 unused Annual Leave days, 5-day limit -> 5 carried forward, 3 expired (AC-1, AC-2, BR-1, BR-2, FR-6) [KEY]

## 1. Test Objective
Verify the KEY year-end behavior: when the leave year ends, `ProcessLeaveYearEndJob` carries forward `MIN(unused, limit)` days as a `carry_forward` ledger entry and forfeits the excess as an `expired` ledger entry, and the new leave year's opening balance equals carry-forward + new entitlement (AC-1, AC-2, BR-1, BR-2, FR-2, FR-6).

## 2. Related Requirements
- User Story: US-LV-008
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-2, FR-6
- Business Rules: BR-1, BR-2
- Data Requirements: Section 7 (leave_ledger carry_forward / expired)

## 3. Preconditions
- Tenant "acme" with Annual Leave configured: `carry_forward_limit = 5`, `carry_forward_expiry_months = 3`.
- Employee "Sam" ends the 2026 leave year with exactly 8 unused Annual Leave days (verified via ledger running total).
- 2027 Annual Leave entitlement for Sam = 14 days (resolved by the US-LV-002 entitlement engine).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Unused balance at 2026 year-end | 8 days | source for carry-forward |
| carry_forward_limit | 5 days | BR-1 cap |
| Expected carried forward | 5 days | MIN(8, 5) |
| Expected expired/forfeited | 3 days | 8 - 5 |
| 2027 new entitlement | 14 days | engine-resolved |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run `ProcessLeaveYearEndJob` for the 2026->2027 boundary in tenant acme | One `carry_forward` ledger entry is created for Sam: `transaction_type='carry_forward'`, days = +5, `transaction_date` = first day of 2027 (FR-6). |
| 2 | Inspect the forfeiture | One `expired` ledger entry is created: `transaction_type='expired'`, days = -3, `transaction_date` = year-end/expiry date (BR-2, FR-6). |
| 3 | Read Sam's opening 2027 balance after accrual | Balance starts at 5 (carry-forward) + 14 (new entitlement) = 19 days (AC-2). |
| 4 | Verify no other employee/type rows were touched unexpectedly | Only Sam's Annual Leave is affected; the math is `carried=MIN(8,5)=5`, `forfeited=8-5=3` (BR-1, BR-2). |

## 6. Postconditions
- Sam has a +5 carry_forward and a -3 expired ledger entry; 2027 opening balance = 19; old-year unused balance is fully resolved (5 carried, 3 expired).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
