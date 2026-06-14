---
id: TC-LV-202
user_story: US-LV-010
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-202: Cancelling a leave that consumed carry-forward days restores them to the correct pool (CONDITIONAL; BR-4)

## 1. Test Objective
Verify that when the cancelled approved leave had consumed carry-forward days, the restoration follows the original allocation logic so carry-forward days are returned to the carry-forward pool (not silently merged into the current-year entitlement). Whether restoration is pool-specific or a general `adjusted` entry is verified against the implemented behavior; if implemented as a single general `adjusted` reversal, that is recorded as a documented simplification (BR-4).

## 2. Related Requirements
- User Story: US-LV-010
- Business Rules: BR-4
- Functional Requirements: FR-3
- Dependencies: US-LV-008 (carry-forward/expiry ledger entries)

## 3. Preconditions
- Tenant "acme".
- Employee "Jane Smith" entered the leave year with 5.00 carry-forward days (a `carry_forward +5.00` ledger entry) plus 14.00 current-year entitlement.
- Jane has an APPROVED future leave R: 6.0 days, where the approval logic consumed 5.0 carry-forward + 1.0 current-year (per the original FIFO allocation).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Carry-forward pool | 5.00 | consumed by R |
| Current-year used | 1.00 | consumed by R |
| R days | 6.0 | Approved future |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Cancel R with a reason | Succeeds; a reversal restoring 6.0 days is written. |
| 2 | Inspect the reversal ledger entry(ies) | EXPECTED (pool-aware): the 5.0 carry-forward days are restored to the carry-forward pool and the 1.0 to the current-year pool, matching the original allocation (BR-4). |
| 3 | (CONDITIONAL) If restoration is a single general `adjusted +6.00` entry | Record this as a documented simplification -- the total balance is correct but the carry-forward vs current-year split is not separately tracked on reversal. Flag for BR-4 follow-up; do NOT treat as a pass for the pool-specific requirement. |
| 4 | Verify carry-forward expiry interaction | If the restored carry-forward still has an expiry window (US-LV-008), the restored days remain subject to the original expiry date (not extended by the cancel). |
| 5 | Re-read the balance | Total balance increases by 6.0; the carry-forward portion is reflected per the implemented behavior. |

## 6. Postconditions
- The cancelled leave's days are restored; the carry-forward pool restoration is verified per BR-4, with any general-`adjusted` simplification explicitly recorded (not a silent gap).

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
