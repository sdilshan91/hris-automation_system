---
id: TC-LV-212
user_story: US-LV-011
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-212: LOP prompt is suppressed when the leave type allows negative balance (boundary)

## 1. Test Objective
Verify that the zero-balance LOP prompt is triggered ONLY when negative balance is not allowed: for a leave type configured with `negative_balance_allowed = true` (e.g. Unpaid Leave within its negative limit), submitting at zero balance does NOT route to LOP but follows the type's normal negative-balance path (AC-1 boundary, BR-1 interplay with US-LV-001/US-LV-003).

## 2. Related Requirements
- User Story: US-LV-011
- Acceptance Criteria: AC-1
- Business Rules: BR-1
- Cross-ref: US-LV-001 (negative_balance_allowed/limit), US-LV-003 (insufficient-balance block)

## 3. Preconditions
- Tenant "acme"; a leave type "Compassionate" has `negative_balance_allowed = true`, `negative_balance_limit = 5`, current balance 0.
- Employee "Jane Smith" authenticated with `Leave.Apply`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Type A | Compassionate (negative allowed, limit 5) | balance 0 |
| Request A | 2 days | within negative limit |
| Type B | Annual (no negative) | balance 0 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Jane submits 2 days of Compassionate (negative allowed) at balance 0 | NO LOP prompt; the request proceeds as a normal request and the type's balance goes negative (-2), within the limit. The request is NOT flagged `is_lop`. |
| 2 | Jane submits 2 days of Annual (no negative) at balance 0 | The LOP prompt IS shown (contrast with step 1), confirming the trigger is governed by `negative_balance_allowed`. |
| 3 | Submit Compassionate exceeding the negative limit (7 days) | Blocked by the US-LV-003 limit rule (not silently converted to LOP); LOP is a separate, explicit path. |

## 6. Postconditions
- The LOP prompt fires only for non-negative-balance types at exhaustion; negative-allowed types use their own path.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
