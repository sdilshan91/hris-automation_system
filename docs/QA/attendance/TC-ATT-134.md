---
id: TC-ATT-134
user_story: US-ATT-010
module: Attendance
priority: high
type: functional
status: pass
created: 2026-06-15
---

# TC-ATT-134: Trend analytics -- 12-month attendance-rate / average-late / overtime / absenteeism series from the monthly summary

## 1. Test Objective
Verify the trend analytics (AC-5, FR-6, BR-5): `GET /api/v1/attendance/reports/trends?months=12` returns line-chart series over the trailing 12 months -- monthly attendance rate, average late arrivals per month, overtime-hours trend, and absenteeism-rate trend -- each point computed from the pre-aggregated `attendance_monthly_summary` (not raw logs, per BR-5), in chronological order with correct values.

## 2. Related Requirements
- User Story: US-ATT-010
- Acceptance Criteria: AC-5 (line charts: monthly attendance rate, average late arrivals, overtime trends over the past 12 months)
- Functional Requirements: FR-6 (trend analytics: monthly attendance rate (12m), avg late arrivals/month, overtime-hours trend, absenteeism-rate trend)
- Business Rules: BR-5 (trend data calculated from the attendance_monthly_summary table for performance)
- API: GET /api/v1/attendance/reports/trends?months=12

## 3. Preconditions
- Tenant "acme"; HR Officer authenticated with `Reports.View.All`.
- attendance_monthly_summary rows exist for the trailing 12 months (2025-07 .. 2026-06) with varied attendance / late / overtime / absence figures.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| months | 12 | trailing window |
| series | attendance_rate, avg_late_arrivals, overtime_hours, absenteeism_rate | four series |
| sample month (2026-05) | rate 88%, avg-late 3.2, OT 410h, absenteeism 6% | seeded |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /reports/trends?months=12` | 200 OK; four series each with 12 chronological points (one per month, oldest -> newest), each point labeled by year-month. |
| 2 | Verify the attendance-rate series | each month's rate = aggregated present-equivalent / expected across the tenant for that month -- matches the monthly summaries (2026-05 = 88%). |
| 3 | Verify the average-late series | each month's value = average late arrivals per employee that month, derived from the summary late counts (2026-05 = 3.2). |
| 4 | Verify the overtime series | each month's value = total approved overtime hours that month (2026-05 = 410h). |
| 5 | Verify the absenteeism series | each month's value = absenteeism rate that month (2026-05 = 6%). |
| 6 | Confirm the data SOURCE (BR-5) | the series are computed from attendance_monthly_summary, not by scanning raw attendance_log -- a missing summary month appears as a gap / 0, not by falling back to a raw recompute. |
| 7 | `months=6` and `months=24` | the window honors the parameter (6 / 24 points); months with no summary are represented consistently (gap vs 0 per the contract). |
| 8 | A tenant with < 12 months of history | returns only the available months (no fabricated future/back-fill points). |

## 6. Postconditions
- The trend series return correct 12-month chronological values sourced from the monthly summary; no data mutated.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Performance test
- [ ] Multi-tenant isolation
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- BR-5 mandates the trend uses the pre-aggregated attendance_monthly_summary for performance; this closes the loop with US-ATT-007 (the summary is the trend source). The average-late series depends on US-ATT-008 late counts being present in the summary (surfaced as seeded; mirrors US-ATT-007 TC-ATT-084 note). **Reported to caller.**
- Missing-month handling (gap vs 0) and the exact absenteeism-rate denominator confirmed against the backend aggregator. **Reported to caller.**
- The smooth line-chart rendering + accessible tooltip/table alternative in TC-ATT-141; tenant isolation (no cross-tenant months) in TC-ATT-ISO-013.
