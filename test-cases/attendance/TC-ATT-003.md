---
id: TC-ATT-003
user_story: US-ATT-001
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-003: Duplicate clock-in is prevented when an open record already exists (negative)

## 1. Test Objective
Verify that an employee who has already clocked in today without clocking out cannot create a second active clock-in. The system must reject the duplicate, leave the existing record untouched, and display the error: "You have already clocked in. Please clock out first."

## 2. Related Requirements
- User Story: US-ATT-001
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists, `active`, Attendance module enabled, timezone `America/New_York`.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`.
- Jordan Lee already has ONE open `attendance_log` for the current local day (clocked in at 09:00 local, `clock_out` null).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | tz America/New_York |
| Existing record | clock_in 09:00 local, clock_out null | Open record |
| Second attempt time | 11:30 local | Same local calendar day |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Reload the dashboard while an open clock-in exists | The Clock-In card shows the running elapsed timer and a "Clock Out" action; a "Clock In" action is NOT offered. |
| 2 | Force a clock-in attempt via the API: `POST /api/v1/attendance/clock-in` | Response status is 409 Conflict (or 422). Body contains a clear error code/message indicating an active clock-in already exists. |
| 3 | Verify the UI message (if attempted from UI) | An inline/toast error reads "You have already clocked in. Please clock out first." |
| 4 | Verify the database | Still exactly ONE open `attendance_log` for Jordan Lee today. No second row was created. The original `clock_in` timestamp is unchanged. |
| 5 | Verify the duplicate detection uses the tenant timezone | The "same day" determination is based on the tenant's configured timezone (America/New_York), not the server's UTC date. |

## 6. Postconditions
- The original open record is intact and unchanged.
- No duplicate `attendance_log` exists for the day.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
