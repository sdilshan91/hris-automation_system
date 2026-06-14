---
id: TC-ATT-121
user_story: US-ATT-009
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-15
---

# TC-ATT-121: LOP-deduction and overtime-pay FORMULAS (AC-2, AC-3, BR-2, BR-3) -- PAYROLL-MODULE responsibility (DEFERRED); verify the attendance-supplied INPUTS are correct and sufficient

## 1. Test Objective
Document and verify the boundary between attendance and payroll for the monetary computations in AC-2/AC-3/BR-2/BR-3. The actual salary math -- `lop_deduction = (basic_salary / total_working_days_in_month) * lop_days` and `overtime_pay = (basic_salary / (total_working_days * shift_hours)) * overtime_hours * multiplier` -- is performed by the PAYROLL engine, which is NOT built. This TC verifies that the attendance side supplies every INPUT those formulas need, with correct values, so that the deferred payroll computation can be exercised the moment the Payroll module lands.

## 2. Related Requirements
- User Story: US-ATT-009
- Acceptance Criteria: AC-2 (LOP deduction = (monthly_salary / total_working_days) * lop_days), AC-3 (overtime_pay = hourly_rate * multiplier * hours)
- Business Rules: BR-2 (lop_deduction formula), BR-3 (overtime_pay formula incl. shift_hours + multiplier)
- Data: §7 total_working_days, lop_days, approved_overtime_minutes, overtime_multiplier_details, total_work_minutes
- Dependency: Payroll module (salary structure: basic_salary; computation engine)

## 3. Preconditions
- Tenant "acme"; monthly summary generated for 2026-05; payroll-data pull (TC-ATT-118) passing.
- A worked example matching the story: employee with 2 LOP days; 10h approved overtime at 1.5x; total_working_days = 22; shift_hours = 8.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| total_working_days | 22 | attendance input (denominator) |
| lop_days | 2.0 | attendance input (TC-ATT-119) |
| approved_overtime_minutes | 600 | attendance input (TC-ATT-120) |
| multiplier | 1.5x | from overtime_multiplier_details |
| shift_hours | 8 | from shift definition (US-ATT-005) |
| basic_salary | (payroll) | NOT an attendance field -- payroll-owned |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | From payroll-data, confirm `total_working_days` (the BR-2 denominator) is present and correct (22) | Available + correct -- payroll can compute LOP deduction per-day rate. |
| 2 | Confirm `lop_days` (the BR-2 multiplicand) is present and correct (2.0, TC-ATT-119) | Available + correct. |
| 3 | Confirm overtime inputs for BR-3 are present: approved_overtime_minutes (600 -> 10h), multiplier breakdown (1.5x), and that shift_hours is resolvable from the shift definition | All inputs needed by the overtime_pay formula are available from attendance + shift data. |
| 4 | LOP-deduction monetary result `(basic_salary/22)*2` | DEFERRED -- PAYROLL-MODULE. Documented expected formula; not computed by attendance (basic_salary is payroll-owned). To be exercised when Payroll lands. |
| 5 | Overtime-pay monetary result `(basic_salary/(22*8))*10*1.5` | DEFERRED -- PAYROLL-MODULE. Documented expected formula; not computed by attendance. |
| 6 | Confirm attendance exposes NO monetary fields (no basic_salary, no deduction amount, no pay amount) | Attendance supplies day/minute/multiplier inputs ONLY; money stays in payroll (clean module boundary, §10 internal integration). |

## 6. Postconditions
- All attendance-side inputs to the AC-2/AC-3 formulas are confirmed present and correct; the monetary computations are recorded as DEFERRED on the Payroll module with their exact expected formulas captured for later verification.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **AC-2/AC-3/BR-2/BR-3 monetary computation is PAYROLL-MODULE responsibility and DEFERRED** -- the Payroll module is not built. This is consistent with how prior attendance stories deferred payroll CONSUMPTION (US-ATT-007 TC-ATT-089 lop_days, US-ATT-006 TC-ATT-074 payroll-ready, US-ATT-008 TC-ATT-107 late-deduction). The attendance side verified here supplies the inputs; the salary math is verified under the Payroll module's own test suite once it exists. **Reported to caller.**
- AMBIGUITY: AC-2 names `monthly_salary / total_working_days` while BR-2 names `basic_salary / total_working_days_in_month` -- the salary base (gross monthly vs basic) differs between the two. This is a PAYROLL-side definition; flagged so the payroll story resolves which base applies. **Reported to caller.**
- AMBIGUITY: AC-3 frames overtime pay as `hourly_rate * 1.5 * hours` while BR-3 derives the hourly rate as `basic_salary / (total_working_days * shift_hours)` -- both reduce to the same shape only if hourly_rate is defined that way. Flagged for the payroll story. **Reported to caller.**
