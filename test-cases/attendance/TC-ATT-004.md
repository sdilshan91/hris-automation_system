---
id: TC-ATT-004
user_story: US-ATT-001
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-004: Clock-in is blocked when geolocation is required but permission is denied (negative)

## 1. Test Objective
Verify that when the tenant policy requires geolocation (`require_geolocation = true`) and the employee denies the browser location-permission prompt, the clock-in is blocked, no `attendance_log` record is created, and the UI explains the requirement.

## 2. Related Requirements
- User Story: US-ATT-001
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3
- Business Rules: BR-2
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists, `active`, Attendance module enabled.
- Tenant attendance settings: `require_geolocation = true` (mandatory).
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`, no clock-in for the current local day.
- The browser/test harness is configured to DENY the geolocation permission prompt.
- The app is served over HTTPS (Geolocation API requires a secure context per NFR-3).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| require_geolocation | true | Mandatory |
| Browser geolocation permission | Denied | User declines prompt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | On the dashboard, click "Clock In" | The browser requests location permission (mandatory policy). |
| 2 | Deny the browser location-permission prompt | The UI does NOT submit the clock-in. A clear message explains that location is required to clock in (e.g., "Location access is required to clock in. Please enable location and try again."). |
| 3 | Confirm no API call created a record | Either no `POST /api/v1/attendance/clock-in` is sent, or if sent without coordinates the server rejects it with 400/422 and an explanatory message. |
| 4 | Verify the database | No `attendance_log` record was created for Jordan Lee today. |
| 5 | Grant permission and retry (positive control) | With permission granted and coordinates supplied, clock-in succeeds (201) and `clock_in_latitude`/`clock_in_longitude` are stored. |

## 6. Postconditions
- No `attendance_log` record exists from the denied attempt.
- Employee can retry after enabling location.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
