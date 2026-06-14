---
id: TC-ATT-014
user_story: US-ATT-002
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-014: Clock-out with no open clock-in record is rejected with a clear error (negative)

## 1. Test Objective
Verify BR-1 / AC-2: an employee who has no open (un-clocked-out) `attendance_log` for the current day cannot clock out. The system must reject the request, create/modify nothing, and display the exact error: "No active clock-in found. Please clock in first or submit a regularization request."

## 2. Related Requirements
- User Story: US-ATT-002
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists, `active`, Attendance module enabled, timezone `America/New_York`.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`.
- Jordan Lee has NO open `attendance_log` for the current local day (never clocked in today).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | tz America/New_York |
| Open record | none | No clock-in today |
| Clock-out attempt time | 17:45 local | Same local day |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the dashboard | The card offers "Clock In" (green); a "Clock Out" action is NOT presented because no open record exists. |
| 2 | Force a clock-out via the API: `POST /api/v1/attendance/clock-out` | Response status is 409 Conflict (or 422). Body carries a clear error code/message: no active clock-in. |
| 3 | Verify the UI message (if attempted from UI) | An inline/toast error reads exactly: "No active clock-in found. Please clock in first or submit a regularization request." |
| 4 | Verify the database | No `attendance_log` row was created or modified for Jordan Lee today. No spurious clock_out written to any other record. |
| 5 | Verify the "today" determination uses tenant timezone | The "no open record today" check is evaluated against the tenant's timezone (America/New_York), not the server UTC date. |

## 6. Postconditions
- No record created or modified.
- The dashboard continues to offer clock-in.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
