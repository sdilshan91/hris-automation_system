---
id: TC-ATT-089
user_story: US-ATT-007
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-089: Loss of Pay calculation -- 3 absent days with no leave coverage yield lop_days = 3 (BR-3)

## 1. Test Objective
Verify BR-3: Loss of Pay (LOP) days are absent days NOT covered by any leave type. For an employee with 3 absent days (scheduled working days, no attendance record, no approved leave), the summary reports lop_days = 3; absent days that ARE covered by approved leave do not add to LOP.

## 2. Related Requirements
- User Story: US-ATT-007
- Functional Requirements: FR-3 (loss_of_pay_days column)
- Business Rules: BR-2 (absent definition), BR-3 (LOP = uncovered absent days), BR-6 (approved leave not counted absent)
- Data Requirements: §7 lop_days

## 3. Preconditions
- Tenant "acme". HR Officer "Priya" authenticated with `Attendance.Read.All`.
- Month 2026-05. Employee "Erik" has, on scheduled working days: 3 days with no attendance record and no approved leave, plus 2 days absent that ARE covered by approved leave. No half-day complications.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| month | 2026-05 | selected period |
| Erik uncovered absences | 3 | working days, no record, no leave |
| Erik leave-covered absences | 2 | approved leave |
| expected lop_days | 3 | only uncovered |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Generate/open the 2026-05 summary for Erik | His row shows total_absent_days for the uncovered absences and total_leave_days = 2. |
| 2 | Verify lop_days | lop_days = 3 (only the absent days with no leave coverage). |
| 3 | Verify the 2 leave-covered days do NOT add to LOP | They are counted as leave (BR-6), not as LOP. |
| 4 | Add one approved leave covering one of the 3 uncovered days, regenerate | lop_days = 2 (the newly covered day reconciles out of LOP). |
| 5 | Verify a fully-present employee | lop_days = 0. |

## 6. Postconditions
- lop_days reflects only absent days uncovered by leave; leave-covered absences are excluded; the value feeds payroll deductions (BR-3, downstream US-ATT-009/Payroll).

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
- LOP feeds payroll deductions (BR-3); the payroll CONSUMPTION of lop_days is owned by US-ATT-009 / the Payroll module -- CONDITIONAL on it. The attendance-side lop_days computation is verified here. **Reported to caller.**
- Leave reconciliation depends on the Leave Management module being operational and leave records up to date (S10); seeded approved-leave data is used here.
