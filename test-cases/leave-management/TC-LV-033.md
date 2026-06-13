---
id: TC-LV-033
user_story: US-LV-002
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-033: Entitlement cannot be negative -- minimum clamped to zero

## 1. Test Objective
Verify that the system enforces a minimum entitlement of zero. Entitlement values cannot go negative regardless of calculation outcomes (e.g., adjustments, pro-rata edge cases, or manual entry). Negative values are rejected at the API level and clamped to zero in calculations.

## 2. Related Requirements
- User Story: US-LV-002
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with `Leave.Configure` permission is authenticated in the "acme" tenant context.
- Leave type "Annual Leave" exists and is active.

## 4. Test Data
| Scenario | Input Value | Expected Result |
|----------|-------------|-----------------|
| Create rule with negative days | -5.00 | Rejected (validation error) |
| Create rule with zero days | 0.00 | Accepted (valid for unpaid leave) |
| Create override with negative days | -10.00 | Rejected (validation error) |
| Pro-rata edge: join Dec 31, 1 day | 20.00 annual | Calculated as ~0.05, stored as 0.05 (not negative) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Attempt to create an entitlement rule with `entitlement_days = -5.00` | API returns 400 Bad Request with validation error: "Entitlement days must be zero or greater." |
| 2 | Attempt to create an entitlement rule with `entitlement_days = 0.00` | API returns 201 Created. Zero is valid (e.g., unpaid leave types). |
| 3 | Attempt to create a per-employee override with `entitlement_days = -10.00` | API returns 400 Bad Request with validation error: "Override entitlement days must be zero or greater." |
| 4 | Attempt to create a per-employee override with `entitlement_days = 0.00` | API returns 201 Created. Zero override is valid. |
| 5 | Create a rule with `entitlement_days = 0.50` for a leave type | Rule saved. |
| 6 | Onboard employee with join date December 31 (1 day remaining in year) | Employee created. |
| 7 | Trigger accrual calculation for this employee | Pro-rata calculation: 0.50 * 1/365 = ~0.00 (rounded to 2dp). Balance is 0.00, not negative. |
| 8 | Verify `leave_ledger` entry amount is >= 0 | Ledger entry amount is 0.00 or a small positive number, never negative. |
| 9 | Attempt to modify an existing rule to `entitlement_days = -1.00` via `PUT /api/v1/leave-entitlement-rules/{id}` | API returns 400 Bad Request with validation error. |

## 6. Postconditions
- No `leave_entitlement_rule` or `leave_entitlement_override` records exist with negative `entitlement_days`.
- No `leave_ledger` accrual entries have negative amounts.
- The minimum entitlement is zero across all calculation paths.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
