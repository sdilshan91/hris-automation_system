---
id: TC-ATT-132
user_story: US-ATT-010
module: Attendance
priority: critical
type: functional
status: pass
created: 2026-06-15
---

# TC-ATT-132: Custom date-range report -- department / location / shift / employee-status / specific-employee filters -> correct daily attendance records

## 1. Test Objective
Verify the custom date-range attendance report (AC-4, FR-4): `GET /api/v1/attendance/reports/custom?from=&to=&...` produces detailed daily attendance records for all matching employees over the selected range, and that the department, location, shift, employee-status, and specific-employee filters combine (AND) to narrow the result set correctly.

## 2. Related Requirements
- User Story: US-ATT-010
- Acceptance Criteria: AC-4 (custom date-range report with optional department/location filters -> daily attendance records for matching employees)
- Functional Requirements: FR-4 (custom date-range reports with filters: department, location, shift, employee status, specific employees)
- API: GET /api/v1/attendance/reports/custom?from=YYYY-MM-DD&to=YYYY-MM-DD&departmentId=&locationId=&shiftId=&employeeStatus=&employeeIds=

## 3. Preconditions
- Tenant "acme"; HR Officer authenticated with `Reports.View.All`.
- Employees across two departments (Eng, Sales), two locations (HQ, Remote), two shifts (Day, Night), mix of ACTIVE / ON_LEAVE statuses, with attendance records over 2026-05-01..2026-05-14.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| from / to | 2026-05-01 / 2026-05-14 | 2-week range |
| departmentId | Engineering | filter |
| locationId | HQ | filter |
| shiftId | Day | filter |
| employeeStatus | ACTIVE | filter |
| employeeIds | [Asha] | specific-employee filter |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /reports/custom?from=2026-05-01&to=2026-05-14` (no filters) | 200 OK; daily attendance records for every tenant employee with data in the range -- one row per employee per day with status / clock-in / clock-out / work-hours / late / overtime. |
| 2 | Add `departmentId=Engineering` | only Engineering employees' daily rows returned; Sales excluded. |
| 3 | Add `locationId=HQ` | further narrows to HQ employees within Engineering (filters AND-combine). |
| 4 | Add `shiftId=Day` | further narrows to Day-shift employees in that set. |
| 5 | Add `employeeStatus=ACTIVE` | ON_LEAVE-status (or terminated) employees excluded from the matching set. |
| 6 | Add `employeeIds=Asha` | result reduces to Asha's daily rows for the range only. |
| 7 | Verify the daily data for Asha | each in-range working day appears with the correct status (present/absent/leave/holiday/weekly-off), clock-in/out, work-minutes, is_late, overtime -- reconciling to the underlying attendance_log + summary. |
| 8 | Invalid range (`from` after `to`) | 400 validation error; no data returned. |
| 9 | Range exceeding the allowed max span (e.g. > tenant report-range cap) | rejected with a clear range-too-large message OR routed to the async/large path (consistent with TC-ATT-133/TC-ATT-139) -- not an unbounded synchronous pull. |

## 6. Postconditions
- The custom report returns the correct filtered daily dataset for the range; no data mutated by the read.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- Department / location / shift / status dimensions come from Core HR + US-ATT-005 (shift) + the attendance records; the report joins them. The exact daily-row source (raw attendance_log vs the daily aggregate) confirmed against the backend. **Reported to caller.**
- Export of this report (CSV/Excel/PDF) in TC-ATT-133; the 5,000-employee/30-day <15s SLA in TC-ATT-139; manager team-scope coercion in TC-ATT-137; tenant isolation in TC-ATT-ISO-013.
- Day boundary uses UTC (tenant-timezone DEFERRED module-wide). **Reported to caller.**
