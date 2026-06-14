---
id: TC-ATT-119
user_story: US-ATT-009
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-15
---

# TC-ATT-119: LOP days computation -- unexcused absences count as LOP, approved leave offsets absences, late-arrival deductions are included in the LOP total

## 1. Test Objective
Verify the attendance-side LOP-days computation that feeds payroll (FR-7, BR-4): `lop_days = absent_days - approved_leave_days_that_cover_absences` (only unexcused absences become LOP), and late-arrival deductions (US-ATT-008, converted to fractional days) are added into the LOP total. This is the correctness of the `lop_days` INPUT; the monetary LOP deduction formula is PAYROLL-MODULE (TC-ATT-121).

## 2. Related Requirements
- User Story: US-ATT-009
- Functional Requirements: FR-7 (lop_days = absent_days - approved_leave_days_that_cover_absences; only unexcused absences)
- Business Rules: BR-4 (late-arrival deductions converted to LOP days and included in the LOP total)
- Data: §7 lop_days, late_deduction_days
- API: GET /api/v1/attendance/payroll-data?month=

## 3. Preconditions
- Tenant "acme"; monthly summary generated for 2026-05.
- Leave Management approved-leave data available for the period (dependency §9).
- US-ATT-008 late policy active so late-deduction days are computed (dependency §9).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Case A absent | 2 days, no approved leave | expect lop_days = 2.0 |
| Case B absent | 3 days, 2 covered by approved leave | expect lop_days = 1.0 |
| Case C late deductions | 3 lates -> 0.5 day, 0 absences | expect lop_days includes 0.5 (late_deduction_days=0.5) |
| Case D | 2 absent + 1 late-deduction day | expect lop_days = 2 + late_deduction_days |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Case A: employee with 2 unexcused absent days, no approved leave; `GET payroll-data?month=2026-05` | `lop_days = 2.0`; total_absent_days = 2.0; no leave offset applied. |
| 2 | Case B: employee with 3 absent days, 2 of which fall on approved-leave dates | `lop_days = 1.0` -- only the 1 unexcused absence is LOP; the 2 leave-covered days are NOT LOP (FR-7). |
| 3 | Case C: employee with 0 absences but 3 lates triggering a 0.5-day late deduction (US-ATT-008) | `late_deduction_days = 0.5`; this 0.5 is included in the LOP total per BR-4 (lop_days reflects the late-deduction contribution). |
| 4 | Case D: employee with 2 unexcused absent days AND a 1-day late deduction | `lop_days` = unexcused-absence days + late_deduction_days (e.g. 2 + 1 = 3.0, exact composition asserted). |
| 5 | Employee fully covered by approved leave (all absences excused) | `lop_days = 0`; absences exist but none are unexcused. |
| 6 | Verify half-day handling -- one half-day absence not covered by leave | `lop_days` reflects the 0.5 fractional day (decimal(4,1)). |

## 6. Postconditions
- lop_days correctly equals unexcused absences (after leave offset) plus included late-deduction days, per FR-7 + BR-4, for each case.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- Approved-leave offset depends on the Leave Management module exposing approved leave for the period; if absent, Case B/E leave-offset is CONDITIONAL on that integration while the no-leave unexcused-absence path (Case A) passes independently. Mirrors US-ATT-007 TC-ATT-091 (approved-only reconciliation). **Reported to caller.**
- Late-deduction days originate from US-ATT-008 (TC-ATT-107 computes the 0.5/1-day flag); this TC verifies their INCLUSION in the LOP total per BR-4. The downstream monetary deduction is PAYROLL-MODULE (TC-ATT-121). **Reported to caller.**
- AMBIGUITY flagged: whether late_deduction_days is also surfaced as a SEPARATE field in addition to being folded into lop_days, or only folded in. §7 lists both lop_days and late_deduction_days as distinct fields; this TC asserts late_deduction_days is exposed AND that BR-4 includes it in the LOP total -- confirm whether payroll double-counts (it must consume one, not both). **Reported to caller.**
