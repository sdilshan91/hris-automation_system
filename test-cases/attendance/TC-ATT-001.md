---
id: TC-ATT-001
user_story: US-ATT-001
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-001: Employee clocks in successfully and a tenant-scoped attendance_log is created (happy path)

## 1. Test Objective
Verify that an authenticated employee with the `Attendance.Clock.Self` permission can clock in from the browser when they have not clocked in today, that a new `attendance_log` record is created with the clock-in timestamp stored in UTC and `tenant_id` taken from the session context, that the IP/user-agent/source audit fields are captured, that the dashboard cache is updated, and that the UI shows a success confirmation with the time converted to the employee's local timezone.

## 2. Related Requirements
- User Story: US-ATT-001
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-5, FR-6, FR-7
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1, BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`, subdomain `acme.yourhrm.com`, timezone `America/New_York`, and the Attendance module enabled.
- Employee "Jordan Lee" exists in "acme", is `active`, and is assigned to an active shift ("Day Shift", expected start 09:00 local).
- Tenant attendance settings: `require_geolocation = false`, `ip_allowlist_enabled = false`, `require_photo = false`.
- Jordan Lee is authenticated (valid JWT with `tenant_id` and `employee_id` claims) and holds the `Attendance.Clock.Self` permission.
- Jordan Lee has NO open or completed `attendance_log` record for the current local calendar day.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant, tz America/New_York |
| User | Jordan Lee (Employee) | Has Attendance.Clock.Self |
| Shift | Day Shift, start 09:00 local | Active assignment |
| Local clock-in time | 08:55 (within shift, before start) | For confirmation display check |
| require_geolocation | false | Geo not enforced |
| ip_allowlist_enabled | false | IP not enforced |
| require_photo | false | Photo not enforced |
| source | WEB | Desktop browser |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the employee dashboard at `https://acme.yourhrm.com/dashboard` | Dashboard loads. The Clock-In card is prominently displayed showing the current shift name ("Day Shift") and expected start time (09:00). The primary action reads "Clock In". |
| 2 | Click the "Clock In" button | A loading indicator appears and the button is disabled to prevent double-submit. |
| 3 | Observe the API call `POST /api/v1/attendance/clock-in` | Request is sent with `X-Tenant-Subdomain: acme` header and Authorization bearer token. Response status is 201 Created. |
| 4 | Inspect the response body | Body contains `attendance_log_id` (UUID), `tenant_id` matching acme's tenant ID, `employee_id` matching Jordan Lee, `clock_in` as a UTC `timestamptz`, `clock_out: null`, `source: "WEB"`. |
| 5 | Verify the created `attendance_log` row in the database | One row exists with the returned `attendance_log_id`; `clock_in` is stored in UTC; `clock_in_ip` and `clock_in_user_agent` are populated; `created_at`/`created_by` audit fields are set; `tenant_id` equals acme. |
| 6 | Verify the dashboard cache | Redis key `att:{acme_tenant_id}:{jordan_employee_id}:{local_date}` reflects the clocked-in status. (If the Redis cache layer is not yet wired, verify the equivalent status is read back correctly from the DB on the next dashboard load — record which path was exercised.) |
| 7 | Observe the UI after the response | The Clock-In card transitions (smooth animation) from "Clock In" to a live elapsed-work timer. A subtle success toast (not a modal) confirms clock-in and displays the clock-in time converted to the employee's local timezone (e.g., "Clocked in at 8:55 AM"). |
| 8 | Verify the displayed time matches UTC->local conversion | The toast/card local time equals the stored UTC `clock_in` converted to America/New_York. |

## 6. Postconditions
- Exactly one open `attendance_log` record exists for Jordan Lee for the current local day (`clock_out` null).
- `clock_in` is persisted in UTC; IP, user agent, and source are recorded for audit.
- Dashboard status reflects "clocked in" (via cache or DB fallback).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
