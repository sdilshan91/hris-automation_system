---
id: TC-ATT-002
user_story: US-ATT-001
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-002: Clock-in succeeds without location when geolocation is optional and permission is denied (alternative path)

## 1. Test Objective
Verify that when the tenant policy treats geolocation as optional (`require_geolocation = false`), an employee who declines the browser location-permission prompt can still clock in successfully, and the resulting `attendance_log` record stores null geolocation fields.

## 2. Related Requirements
- User Story: US-ATT-001
- Acceptance Criteria: AC-4
- Functional Requirements: FR-1
- Business Rules: BR-2

## 3. Preconditions
- Tenant "acme" exists, `active`, Attendance module enabled.
- Tenant attendance settings: `require_geolocation = false`, `ip_allowlist_enabled = false`, `require_photo = false`.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`, and has no clock-in for the current local day.
- The browser/test harness is configured to DENY the geolocation permission prompt.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| require_geolocation | false | Optional |
| Browser geolocation permission | Denied | User declines prompt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | On the dashboard, click "Clock In" | Because geolocation is optional, the UI either skips the prompt or, if it prompts, gracefully proceeds when permission is denied. No blocking error is shown. |
| 2 | Deny the browser location-permission prompt (if shown) | Clock-in continues without aborting. |
| 3 | Observe the API call `POST /api/v1/attendance/clock-in` | Request is sent with no latitude/longitude in the payload. Response status is 201 Created. |
| 4 | Inspect the created `attendance_log` record | `clock_in_latitude` and `clock_in_longitude` are null; `clock_in` (UTC), `tenant_id`, `employee_id`, IP, and user agent are populated. |
| 5 | Observe the UI | Success toast confirms clock-in. No map preview is shown (no coordinates captured). The elapsed-work timer starts. |

## 6. Postconditions
- One open `attendance_log` record exists with null geolocation fields.
- No location data was requested as mandatory.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
