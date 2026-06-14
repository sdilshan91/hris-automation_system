---
id: TC-ATT-085
user_story: US-ATT-007
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-085: Drill-down -- clicking an employee shows the day-by-day breakdown with clock-in/out, status, and regularizations (happy path)

## 1. Test Objective
Verify AC-2: when the HR Officer clicks an employee row in the monthly summary, the system shows a day-by-day breakdown for that employee for the selected month (`GET /api/v1/attendance/summary/monthly/{employeeId}?month=YYYY-MM`), with each day's clock-in/out times, status (present/absent/leave/holiday/weekly-off), and any regularizations applied, tenant-scoped.

## 2. Related Requirements
- User Story: US-ATT-007
- Acceptance Criteria: AC-2
- UI/UX Notes: §8 (drill-down calendar/grid view per day with color coding)
- Business Rules: BR-7 (regularized attendance treated as normal)

## 3. Preconditions
- Tenant "acme". HR Officer "Priya" authenticated with `Attendance.Read.All`.
- Month 2026-05 summary generated. Asha (employeeId known) has, within May: normal clock-in/out days, one APPROVED regularized day (originally missing, now corrected times), one approved leave day, one absent day, the month's public holidays and weekly-offs.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| employeeId | Asha | drill-down target |
| month | 2026-05 | selected period |
| Day types present | present, absent, leave, holiday, weekly-off, regularized | full mix |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In the May summary, click Asha's row | The drill-down opens; `GET /summary/monthly/{ashaId}?month=2026-05` returns 200 with a per-day breakdown for the whole month. |
| 2 | Verify a normal working day | Shows clock_in/clock_out times (in the employee's local timezone display), status = present, and work hours for the day. |
| 3 | Verify the regularized day | Shows the corrected (regularized) clock-in/out times and is marked as present/regularized -- treated identically to a normal day (BR-7), with a regularization indicator. |
| 4 | Verify the approved-leave day | Status = leave (leave type/label), not counted as absent. |
| 5 | Verify the absent day | Status = absent (no clock record, scheduled working day, no leave). |
| 6 | Verify holiday and weekly-off days | Status = holiday / weekly-off respectively; not counted as present or absent. |
| 7 | Verify drill-down totals reconcile with the summary row | The day-by-day counts (present/absent/leave/holiday/weekly-off) sum to Asha's summary-row totals from TC-ATT-084. |

## 6. Postconditions
- HR sees an accurate, tenant-scoped day-by-day breakdown for the selected employee/month that reconciles with the summary row; regularized days appear as normal attendance.

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
- Day classification uses UTC day boundaries (per the attendance vault; tenant-timezone infra DEFERRED module-wide). If tenant-local day boundaries are required for the drill-down, that is the same deferred concern. **Reported to caller.**
- Regularization data integrates with US-ATT-003/US-ATT-004 (approved regularizations only); the drill-down surfaces them, it does not create them.
