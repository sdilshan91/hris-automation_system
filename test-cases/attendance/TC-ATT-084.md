---
id: TC-ATT-084
user_story: US-ATT-007
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-084: Monthly summary table -- one row per employee with all summary columns for a full month with varied data (happy path)

## 1. Test Objective
Verify AC-1/FR-3: an HR Officer who opens the monthly attendance summary page and selects a completed month gets a Notion-style table with exactly one row per employee, each row showing all per-employee columns -- total_present_days, total_absent_days, total_late_count, total_early_departure_count, total_work_hours (from total_work_minutes), total_overtime_hours (from total_overtime_minutes), total_leave_days, total_holidays, total_weekly_offs, lop_days -- computed from varied source data (present, absent, late, overtime, leave, holiday), tenant-scoped.

## 2. Related Requirements
- User Story: US-ATT-007
- Acceptance Criteria: AC-1
- Functional Requirements: FR-3 (per-employee columns)
- Data Requirements: §7 attendance_monthly_summary

## 3. Preconditions
- Tenant "acme". HR Officer "Priya" authenticated with `Attendance.Read.All`. Attendance module enabled.
- Month 2026-05 is complete and its summary has been generated (Hangfire monthly job ran, or generated on demand per TC-ATT-086).
- Seeded May data: Asha = 20 present, 1 absent (no leave), 3 late, 1 early departure, 6h approved overtime, 1 approved leave day; Carl = full attendance, 0 absent, 0 late; Dana = 5 absent (no leave), 2 approved leave days. The month has 2 public holidays and weekly-offs per each employee's shift.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| month | 2026-05 | completed month |
| Columns expected | present, absent, late, early-departure, work-hours, overtime-hours, leave, holidays, weekly-offs, lop | FR-3 + §7 |
| Asha | present=20, absent=1, late=3, early=1, OT=6h, leave=1 | varied |
| Carl | present=full, absent=0, late=0 | full attendance |
| Dana | absent=5, leave=2, lop=5 | high absence |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya, navigate to the monthly summary page and select 2026-05 (`GET /api/v1/attendance/summary/monthly?month=2026-05`) | 200; a table with exactly one row per employee (no duplicate rows), tenant-scoped to acme. |
| 2 | Verify Asha's row | present=20, absent=1, late_count=3, early_departure_count=1, work_hours derived from total_work_minutes, overtime_hours=6 (from approved overtime minutes only), leave_days=1, holidays=2 reflected. |
| 3 | Verify Carl's row (full attendance) | absent=0, late=0, lop=0; present equals his scheduled working days for the month. |
| 4 | Verify Dana's row | absent=5, leave_days=2; lop_days reflects only absent days not covered by leave (see TC-ATT-089 for the LOP rule detail). |
| 5 | Verify every FR-3 column is present and populated | All ten columns render with numeric values (decimal where §7 allows half-days); work/overtime shown as hours derived from stored minutes, accurate to the minute (NFR-5). |
| 6 | Verify the summary banner (§8) | Shows total employees, average attendance %, and total LOP days for the month. |

## 6. Postconditions
- HR sees a correct, tenant-scoped one-row-per-employee monthly summary with all FR-3 columns for the selected completed month; no source data is mutated by viewing.

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
- Work/overtime are stored as minutes (§7 total_work_minutes / total_overtime_minutes) and displayed as hours -- assert the minute-accurate conversion (NFR-5).
- Late-arrival and early-departure counts (total_late_count / total_early_departure_count) originate from US-ATT-008 detection; this TC asserts the columns surface the counts as seeded. The end-to-end late/early flagging is owned by US-ATT-008. **Reported to caller** (dependency).
- Overtime hours reflect APPROVED overtime only per §7 (total_overtime_minutes = approved); integrates with US-ATT-006.
- Whether employees with zero activity (e.g. new hires) appear as zeroed rows or are omitted follows the report's documented rule; assert against it and flag to the BA if unspecified.
