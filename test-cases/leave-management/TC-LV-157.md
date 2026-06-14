---
id: TC-LV-157
user_story: US-LV-008
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-157: Carry-forward / forfeiture math boundaries -- MIN(unused, limit) and excess>0 (BR-1, BR-2)

## 1. Test Objective
Verify the carry-forward and forfeiture formulas at their boundaries: carry-forward = `MIN(unused_balance, carry_forward_limit)`; forfeiture = `unused_balance - carry_forward_amount` only when positive (BR-1, BR-2). Covers unused below, equal to, and above the limit, plus a zero-unused case.

## 2. Related Requirements
- User Story: US-LV-008
- Business Rules: BR-1, BR-2
- Acceptance Criteria: AC-1

## 3. Preconditions
- Tenant "acme"; Annual Leave `carry_forward_limit = 5`.
- Four employees with distinct year-end unused balances.

## 4. Test Data
| Employee | Unused | Expected carry-forward | Expected forfeiture |
|----------|--------|------------------------|---------------------|
| A | 3 (below limit) | 3 | 0 |
| B | 5 (at limit) | 5 | 0 |
| C | 8 (above limit) | 5 | 3 |
| D | 0 (none) | 0 | 0 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run the year-end job; inspect employee A (unused 3 < limit 5) | carry_forward = +3; NO expired entry (forfeiture 0, BR-2 "if > 0"). |
| 2 | Inspect employee B (unused 5 = limit 5) | carry_forward = +5; NO expired entry (exact-limit boundary). |
| 3 | Inspect employee C (unused 8 > limit 5) | carry_forward = +5; expired = -3 (BR-1, BR-2). |
| 4 | Inspect employee D (unused 0) | NO carry_forward and NO expired entry -- nothing to process. |

## 6. Postconditions
- Carry-forward equals MIN(unused, limit); expired entries appear only when excess > 0.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
