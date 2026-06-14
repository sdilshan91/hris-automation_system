---
id: TC-ATT-015
user_story: US-ATT-002
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-015: Clock-out on an already-completed record is rejected; the original record is untouched (negative)

## 1. Test Objective
Verify BR-1: once a record for the day has been clocked out (`clock_out` populated, status terminal), a second clock-out must be rejected. The system must not overwrite the existing `clock_out`/`total_work_minutes`, must not double-count hours, and must surface a clear "no active clock-in" style error (the record is no longer open).

## 2. Related Requirements
- User Story: US-ATT-002
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-2
- Business Rules: BR-1
- Non-Functional Requirements: NFR-3 (no partial/duplicate mutation)

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, tz `America/New_York`.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`.
- Jordan Lee has ONE COMPLETED `attendance_log` for today: `clock_in` 09:00 local, `clock_out` 17:00 local, `total_work_minutes = 420`, `status = COMPLETE`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Existing record | clock_in 09:00, clock_out 17:00, total 420, COMPLETE | Already closed |
| Second clock-out attempt | 18:30 local | Same day |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the dashboard | The card shows the completed-day summary, not a "Clock Out" action (there is no open record). |
| 2 | Force `POST /api/v1/attendance/clock-out` again | Response 409 Conflict (or 422); body indicates there is no active/open clock-in to close. |
| 3 | Verify the UI message (if attempted from UI) | Error: "No active clock-in found. Please clock in first or submit a regularization request." (no second open record exists). |
| 4 | Verify the database | The original record is unchanged: `clock_out` still 17:00, `total_work_minutes` still 420, `status` still COMPLETE. No second `clock_out` overwrite, no recompute. |
| 5 | Confirm no new row | No additional `attendance_log` row was created for the day. |

## 6. Postconditions
- The completed record is intact; no double clock-out or recomputation occurred.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
