---
id: TC-ATT-129
user_story: US-ATT-010
module: Attendance
priority: critical
type: functional
status: pass
created: 2026-06-15
---

# TC-ATT-129: Real-time dashboard KPIs -- clock in several employees -> expected / clocked-in / pending / on-leave / absent / attendance% computed correctly

## 1. Test Objective
Verify the attendance dashboard KPI overview (AC-1, FR-1, BR-1, BR-2): for a given tenant + date, `GET /api/v1/attendance/dashboard?date=&scope=` returns the today's-attendance KPIs -- expected headcount, clocked-in count, pending (not-yet-clocked-in) count, on-leave count, absent count, and a live attendance percentage -- with each count reconciling to the seeded workforce state, and the expected headcount and percentage following the BR-1/BR-2 formulas.

## 2. Related Requirements
- User Story: US-ATT-010
- Acceptance Criteria: AC-1 (real-time overview: expected today, clocked-in, not-yet-clocked-in, on-leave, live attendance %)
- Functional Requirements: FR-1 (real-time dashboard with today's KPIs: expected, clocked-in, pending, on-leave, absent, attendance %)
- Business Rules: BR-1 (expected headcount = active employees - full-day approved leave - employees at holiday locations), BR-2 (attendance % = clocked_in / expected * 100)
- Data: §7 dashboard KPIs (expected / clocked_in / on_leave / absent / attendance_pct)
- API: GET /api/v1/attendance/dashboard?date=2026-06-15&scope=all

## 3. Preconditions
- Tenant "acme"; Attendance enabled; HR Officer "Priya" authenticated with `Attendance.Read.All` + `Reports.View.All`.
- 20 active employees today. 2 are on full-day approved leave. 1 is at a location where today is a public holiday. The remaining 17 are the expected headcount.
- 12 of the expected-17 have clocked in; 5 have not yet clocked in (and are not on leave / not at a holiday location).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| date | 2026-06-15 | today |
| scope | all | HR all-scope |
| active employees | 20 | tenant-scoped |
| full-day approved leave | 2 | excluded from expected (BR-1) |
| holiday-location employees | 1 | excluded from expected (BR-1) |
| expected headcount | 17 | 20 - 2 - 1 |
| clocked-in | 12 | have an attendance_log today |
| pending (not clocked in) | 5 | 17 - 12 |
| on-leave | 2 | full-day approved |
| attendance % | 70.6% | 12 / 17 * 100 (BR-2) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya, `GET /api/v1/attendance/dashboard?date=2026-06-15&scope=all` | 200 OK; KPI object with expected, clocked_in, pending, on_leave, absent, attendance_pct fields. |
| 2 | Verify `expected` | expected = 17 (active 20 minus 2 full-day leave minus 1 holiday-location employee per BR-1), NOT the raw active count of 20. |
| 3 | Verify `clocked_in` | clocked_in = 12 (count of employees with a clock-in record today, tenant-scoped). |
| 4 | Verify `pending` | pending = 5 (expected minus clocked_in; employees who should be present but have no clock-in yet). |
| 5 | Verify `on_leave` | on_leave = 2 (full-day approved leave for today). |
| 6 | Verify `absent` | absent = pending where not on leave / not holiday = 5 (per §7 "not clocked in, not on leave"). |
| 7 | Verify `attendance_pct` | attendance_pct = 70.6 (12 / 17 * 100 per BR-2), rounded per the response contract. |
| 8 | Clock in one more of the 5 pending employees, re-request the dashboard | clocked_in -> 13, pending -> 4, attendance_pct -> 76.5 (13/17); the KPIs reflect the new state ("updated in real-time" -- DB recompute path; Redis/SignalR refresh in TC-ATT-138/TC-ATT-130). |
| 9 | Edge: a tenant/date with NO active employees (expected = 0) | attendance_pct does NOT divide-by-zero -- returns 0 (or a clear "no expected headcount" sentinel), not NaN/error. |

## 6. Postconditions
- The dashboard KPIs reflect the live workforce state for the tenant + date; no attendance data is mutated by the read.

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
- BR-1 "holiday-location" exclusion depends on the public-holiday source (US-LV-007 calendar) integrated into the expected-headcount computation; the active-minus-leave path is verified unconditionally here, the holiday-location exclusion is CONDITIONAL on that integration (mirrors US-ATT-006 TC-ATT-069 / US-ATT-007 TC-ATT-092 holiday-source deferrals). **Reported to caller.**
- The on_leave count depends on Leave Management exposing approved full-day leave for the date; the no-leave path is independent, the leave-offset branch CONDITIONAL on that integration (mirrors US-ATT-009 TC-ATT-119). **Reported to caller.**
- Manager team-scope vs HR all-scope of `scope=` is verified in TC-ATT-137; the Redis-cached KPI path + DB fallback in TC-ATT-138; the <2s P95 SLA in TC-ATT-139; tenant isolation in TC-ATT-ISO-013.
- Day boundary uses UTC (tenant-timezone infra DEFERRED module-wide, per the attendance vault). **Reported to caller.**
