---
id: TC-LV-217
user_story: US-LV-011
module: Leave Management
priority: critical
type: integration
status: draft
created: 2026-06-14
---

# TC-LV-217: lop-summary endpoint returns the LOP data payroll consumes; deduction = (salary/working_days)*lop_days (deduction calc CONDITIONAL on Payroll module)

## 1. Test Objective
Verify the LOP→payroll integration point (AC-4, FR-5): `GET /api/v1/leaves/lop-summary?employeeId={id}&from={date}&to={date}` returns the employee's LOP day count and entries for the period (the data the payroll engine queries), and that the documented deduction formula `(monthly_salary / working_days) * lop_days` is correctly applied. The leave-side lop-summary contract is verified live; the actual payslip line-item calculation DEPENDS on the Payroll module (US-PAYROLL-*) and is recorded CONDITIONAL.

## 2. Related Requirements
- User Story: US-LV-011
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5
- Business Rules: BR-2 (formula tenant-configurable)
- Dependency: US-PAYROLL-* (deduction calc / payslip) — CONDITIONAL
- Test Hint §11 (payroll integration)

## 3. Preconditions
- Tenant "acme"; LOP type exists; employee "Mark Otieno" has 3 LOP days in the target month (mix of HR-assigned and/or system-generated).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Period | one calendar month | from/to |
| lop_days | 3 | expected summary total |
| monthly_salary | 30,000 | example |
| working_days | 22 | example |
| Expected deduction | (30000/22)*3 ≈ 4090.91 | per BR-2 default |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/leaves/lop-summary?employeeId=Mark&from&to` | 200; the response lists Mark's LOP entries for the period and a total of 3 LOP days, sourced from `leave_request` rows with `is_lop = true` (employee_request + system_generated + hr_assigned + compulsory). This is the leave-side contract — verified live. |
| 2 | Verify tenant + employee scope of the summary | Only Mark's acme LOP entries are returned; no other employee's or tenant's LOP days are included. |
| 3 | (CONDITIONAL on Payroll) Run/derive the deduction from the summary | The payroll engine computes `(30000/22)*3 ≈ 4090.91` and renders it as a payslip line item. Mark this step CONDITIONAL on US-PAYROLL-*; the leave module's responsibility ends at supplying the correct lop_days via lop-summary. |
| 4 | (CONDITIONAL, BR-2) Switch the tenant formula to gross/calendar-days basis | The deduction recomputes per the alternate formula; confirms the formula is tenant-configurable. CONDITIONAL on payroll config. |

## 6. Postconditions
- lop-summary returns the correct, tenant/employee-scoped LOP day data for payroll; the deduction calculation is conditionally verified pending the payroll module.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
