---
id: TC-LV-029
user_story: US-LV-002
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-029: Pro-rata entitlement calculation for mid-year joiners

## 1. Test Objective
Verify that when a new employee is onboarded mid-year, their leave entitlement is pro-rated based on their joining date and the configured accrual frequency. Specifically, an employee joining on July 1 with a 20-day annual entitlement should receive 10 days. Rounding is to two decimal places using half-up rounding.

## 2. Related Requirements
- User Story: US-LV-002
- Acceptance Criteria: AC-4
- Functional Requirements: FR-3
- Assumptions & Constraints: Section 10 (rounding 2dp half-up)

## 3. Preconditions
- Tenant "acme" exists with leave year starting January 1 (calendar year).
- A user with `Leave.Configure` permission is authenticated in the "acme" tenant context.
- Leave type "Annual Leave" exists with accrual frequency = "Yearly".
- Entitlement rule: "Annual Leave" default = 20 days/year.
- Hangfire accrual job infrastructure is running.

## 4. Test Data
| Employee | Join Date | Annual Entitlement | Remaining Months | Expected Pro-Rata | Rounding |
|----------|-----------|-------------------|-----------------|-------------------|----------|
| Employee A | 2026-07-01 | 20.00 | 6/12 | 10.00 | Exact |
| Employee B | 2026-04-01 | 20.00 | 9/12 | 15.00 | Exact |
| Employee C | 2026-10-15 | 20.00 | 2.5/12 | 4.17 | Half-up from 4.1667 |
| Employee D | 2026-01-01 | 20.00 | 12/12 | 20.00 | Full year |
| Employee E | 2026-11-01 | 15.00 | 2/12 | 2.50 | Exact |
| Employee F | 2026-12-31 | 20.00 | 0.03/12 | 0.05 | Minimal (1 day) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Onboard Employee A with join date 2026-07-01 in Engineering department | Employee created with joining_date = 2026-07-01. |
| 2 | Trigger Hangfire accrual calculation for Employee A | Job executes successfully. |
| 3 | Query Employee A leave balance for "Annual Leave" | Balance = 10.00 days (20 * 6/12). |
| 4 | Verify `leave_ledger` entry for Employee A | Entry type "accrual", amount = 10.00, reason contains "pro-rata". |
| 5 | Onboard Employee B with join date 2026-04-01 | Employee created. |
| 6 | Trigger accrual calculation for Employee B | Balance = 15.00 days (20 * 9/12). |
| 7 | Onboard Employee C with join date 2026-10-15 | Employee created. |
| 8 | Trigger accrual calculation for Employee C | Balance = 4.17 days (20 * 2.5/12 = 4.1667, rounded half-up to 4.17). |
| 9 | Verify rounding precision: the value stored in `leave_ledger` and displayed in UI is 4.17, not 4.16 or 4.1667 | Stored as numeric(5,2) = 4.17. |
| 10 | Onboard Employee D with join date 2026-01-01 (start of year) | Employee created. |
| 11 | Trigger accrual calculation for Employee D | Balance = 20.00 days (full year, no pro-rata needed). |
| 12 | Onboard Employee E with join date 2026-11-01, different rule entitlement = 15 days | Employee created. |
| 13 | Trigger accrual calculation for Employee E | Balance = 2.50 days (15 * 2/12). |

## 6. Postconditions
- All mid-year joiners have correctly pro-rated leave balances.
- `leave_ledger` entries reflect the pro-rated amounts with "accrual" type.
- Rounding follows half-up to two decimal places consistently.
- Full-year employees receive the complete entitlement without pro-rata adjustment.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
