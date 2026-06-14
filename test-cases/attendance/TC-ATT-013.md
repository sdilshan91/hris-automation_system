---
id: TC-ATT-013
user_story: US-ATT-002
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-013: Employee clocks out successfully, total work hours are auto-calculated, and a summary is shown (happy path)

## 1. Test Objective
Verify that an authenticated employee with an open clock-in record can clock out from the browser; that `attendance_log.clock_out` is set to the current server-side UTC timestamp; that `total_work_minutes` is computed as `(clock_out - clock_in) - auto_break_deduction`; that the record is marked `COMPLETE` when within shift bounds; that audit and source-IP fields are stamped; and that the UI shows a summary card with clock-in time, clock-out time, total hours, and any badge, all rendered in the employee's local timezone.

## 2. Related Requirements
- User Story: US-ATT-002
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2, FR-3, FR-5
- Non-Functional Requirements: NFR-2 (accuracy to the minute), NFR-5 (local-tz display)
- Business Rules: BR-1, BR-2

## 3. Preconditions
- Tenant "acme" exists, `active`, subdomain `acme.yourhrm.com`, timezone `America/New_York`, Attendance module enabled.
- Employee "Jordan Lee" is `active`, authenticated (valid JWT with `tenant_id` and `employee_id`), holds `Attendance.Clock.Self`.
- Jordan Lee is assigned to "Day Shift" (standard 8h = 480 min; break deduction 60 min for shifts > 6h).
- Jordan Lee has ONE open `attendance_log` for the current local day: `clock_in` = 09:00 local, `clock_out` null.
- Geolocation NOT required on clock-out for this tenant; IP allowlist off.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | tz America/New_York |
| Open record clock_in | 09:00 local | UTC-stored |
| Clock-out time | 17:45 local | 8h 45m raw span = 525 min |
| Auto-break deduction | 60 min | Shift > 6h |
| Expected total_work_minutes | 465 | 525 - 60 |
| Expected status | COMPLETE | Within shift bounds (465 < 480 standard but >= min; see TC-ATT-016 for short-day edge) |
| Expected display | "7h 45m" | 465 min |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load `https://acme.yourhrm.com/dashboard` while clocked in | The card shows a live elapsed timer and a warm-colored "Clock Out" action (distinct from the green "Clock In"). |
| 2 | Click "Clock Out" | A loading indicator appears; the button is disabled to prevent double-submit. |
| 3 | Observe `POST /api/v1/attendance/clock-out` | Sent with `X-Tenant-Subdomain: acme` + bearer token. Response 200 OK. |
| 4 | Inspect the response body | Contains the same `attendance_log_id`, `clock_out` as UTC `timestamptz` (server time, not client-reported), `total_work_minutes: 465`, `status: "COMPLETE"`, `overtime_minutes: null`. |
| 5 | Verify the DB row | The existing row is UPDATED (not duplicated): `clock_out` set in UTC, `clock_out_ip` populated, `total_work_minutes = 465`, `status = COMPLETE`, `updated_at`/`updated_by` audit fields set, `tenant_id` still acme. |
| 6 | Verify the break deduction | `total_work_minutes` = raw span (525) minus the configured 60-min break = 465, confirming FR-3. |
| 7 | Verify the status cache | Key `att:{acme_id}:{jordan_id}:{local_date}` reflects clocked-out + total hours. (If Redis is not yet wired, verify the equivalent clocked-out status is read back from the DB on the next dashboard load; record which path was exercised.) |
| 8 | Observe the UI summary | A summary card fades in showing clock-in 9:00 AM, clock-out 5:45 PM (local tz), total "7h 45m", and a green "Complete" status pill. The "Clock Out" action is replaced by the day's completed state. |
| 9 | Verify timezone conversion | All displayed times equal the stored UTC values converted to America/New_York; the calculation itself uses UTC instants. |

## 6. Postconditions
- The day's `attendance_log` is closed (`clock_out` set), `total_work_minutes = 465`, `status = COMPLETE`.
- No new row created; exactly one (now completed) record exists for the day.
- Dashboard reflects "clocked out / complete" with the computed total.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
