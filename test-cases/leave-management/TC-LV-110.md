---
id: TC-LV-110
user_story: US-LV-006
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-110: Summary card values and progress bar are accurate

## 1. Test Objective
Verify that each balance card's displayed entitlement, used, pending, and balance values match the underlying ledger data and that the progress bar fill reflects the used-vs-entitlement ratio (AC-1, FR-2, BR-1).

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-1
- Functional Requirements: FR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated.
- Annual Leave: entitlement 14, carry-forward 2, used 5 (from approved requests), 0 expired, 0 adjustments, 1 pending request of 2 days.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Entitlement | 14 | annual_entitlement |
| Carry Forward | 2 | from prior year |
| Used | 5 | ledger 'Used' entries |
| Pending | 2 | open pending request |
| Expected balance | 11 | 14 + 2 - 5 - 0 + 0 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the dashboard and read the Annual card | entitlement=14, carryForward=2, used=5, pending=2 displayed. |
| 2 | Read the balance value | balance=11 (per BR-1: 14 + 2 - 5 - 0 + 0); pending (2) is shown separately and is NOT subtracted from balance. |
| 3 | Inspect the progress bar | Fill represents used (5) relative to entitlement+carryForward (16) -- approximately 31% -- and the numeric values remain visible alongside it. |
| 4 | Cross-check against `GET /api/v1/leaves/my-balance` payload | The card values equal the API's `entitlement`, `used`, `pending`, `balance`, `carryForward`, `expired` fields exactly. |

## 6. Postconditions
- Displayed values reconcile with the ledger; no data mutated.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
