---
id: TC-ATT-135
user_story: US-ATT-010
module: Attendance
priority: medium
type: functional
status: draft
created: 2026-06-15
---

# TC-ATT-135: Pre-built report catalog -- Daily / Weekly Summary / Monthly Summary / Departmental Comparison / Late Arrival / Overtime / Absenteeism all available and produce correct data

## 1. Test Objective
Verify the pre-built report catalog (FR-3): the system exposes the seven pre-built report types -- Daily Attendance, Weekly Summary, Monthly Summary, Departmental Comparison, Late Arrival, Overtime, Absenteeism -- each runnable for a period and returning the correct dataset, so HR can pick a ready report without building a custom one.

## 2. Related Requirements
- User Story: US-ATT-010
- Functional Requirements: FR-3 (pre-built reports: Daily Attendance, Weekly Summary, Monthly Summary, Departmental Comparison, Late Arrival Report, Overtime Report, Absenteeism Report)
- API: the report endpoints (e.g. /reports/custom with a reportType, or dedicated /reports/{type}); department-comparison verified in TC-ATT-131

## 3. Preconditions
- Tenant "acme"; HR Officer authenticated with `Reports.View.All`.
- Generated attendance data + monthly summaries for the period, including some late arrivals, approved overtime, and unplanned absences.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| report types | daily, weekly, monthly, departmental, late, overtime, absenteeism | 7 pre-built |
| period | 2026-05 (or a day/week per type) | per report |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Request the report catalog / list of pre-built report types | all seven types are offered and selectable. |
| 2 | Run Daily Attendance for a date | per-employee present/absent/late/leave for that day. |
| 3 | Run Weekly Summary for a week | per-employee weekly present/absent/work-hours/overtime totals. |
| 4 | Run Monthly Summary for 2026-05 | reconciles to the US-ATT-007 monthly summary table (one row per employee with the standard columns). |
| 5 | Run Departmental Comparison | per-department attendance rate (delegates to TC-ATT-131 contract). |
| 6 | Run Late Arrival Report | employees with late arrivals + late counts/minutes for the period (sourced from US-ATT-008). |
| 7 | Run Overtime Report | approved/pending/rejected overtime by employee for the period (sourced from US-ATT-006; mirrors TC-ATT-079). |
| 8 | Run Absenteeism Report | employees ranked by unplanned absences / absenteeism rate for the period. |
| 9 | Each report supports the standard filters + export | the FR-4 filters (where applicable) and FR-5 CSV/Excel/PDF export apply to each pre-built report (export verified in TC-ATT-133). |

## 6. Postconditions
- All seven pre-built reports are available and return correct, period-scoped data; no data mutated.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Performance test
- [ ] Multi-tenant isolation
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- Several pre-built reports re-use existing module outputs: Monthly Summary = US-ATT-007 (TC-ATT-084), Overtime Report = US-ATT-006 (TC-ATT-079), Late Arrival = US-ATT-008 (TC-ATT-112). This TC verifies they are surfaced under the unified report catalog with consistent period/filter/export behaviour; the detailed per-report math is owned by those stories' TCs. **Reported to caller.**
- The exact endpoint shape (a `reportType` param on /reports/custom vs dedicated routes) confirmed against the backend; absenteeism-rate denominator shared with TC-ATT-134. **Reported to caller.**
- Manager team-scope vs HR all-scope per report in TC-ATT-137; tenant isolation in TC-ATT-ISO-013.
