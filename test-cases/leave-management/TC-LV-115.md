---
id: TC-LV-115
user_story: US-LV-006
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-115: Balance correctness across carry-forward, expiry, and adjustments (BR-1 formula)

## 1. Test Objective
Verify that the displayed balance always equals `entitlement + carry_forward - used - expired + adjustments` across non-trivial combinations, including negative adjustments and expired carry-forward (BR-1, FR-2, FR-5).

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-1
- Functional Requirements: FR-2, FR-5
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated.
- Annual Leave 2026 ledger: entitlement 14, carryForward 5, used 7, expired 1 (unused carry-forward expired), adjustments +2 and -1 (net +1).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Entitlement | 14 | -- |
| Carry Forward | 5 | -- |
| Used | 7 | -- |
| Expired | 1 | -- |
| Adjustments (net) | +1 | +2 and -1 |
| Expected balance | 12 | 14 + 5 - 7 - 1 + 1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the dashboard and read the Annual card | entitlement=14, carryForward=5, used=7, expired=1 displayed. |
| 2 | Compute the expected balance per BR-1 | 14 + 5 - 7 - 1 + 1 = 12. |
| 3 | Compare to the card balance and the `my-balance` API `balance` field | Both equal 12; expired and adjustments are reflected (not ignored). |
| 4 | Open the ledger and re-derive balance-after from the entries | The running balance-after of the final entry equals 12, reconciling card and ledger. |

## 6. Postconditions
- Balance reconciles to the BR-1 formula including expiry and signed adjustments.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
