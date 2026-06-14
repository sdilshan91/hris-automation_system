---
id: TC-ATT-017
user_story: US-ATT-002
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-017: Short-day detection on clock-out — below shift minimum is flagged SHORT_DAY for HR review (AC-4)

## 1. Test Objective
Verify AC-4 / FR-4 / BR-4: when total work minutes are below the shift's minimum required hours, the record is flagged `SHORT_DAY` for HR review, `overtime_minutes` is null, and the summary card shows an amber "Short Day" pill. Worked example from the story test hints: 4h worked on an 8h shift → short day.

## 2. Related Requirements
- User Story: US-ATT-002
- Acceptance Criteria: AC-4
- Functional Requirements: FR-2, FR-4
- Business Rules: BR-2, BR-4

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, tz `America/New_York`.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Clock.Self`.
- "Day Shift": standard 480 min (8h); minimum required = 360 min (6h) for short-day determination.
- Break deduction assumed 0 for this 4h case (document the configuration so the 240-min expectation is exact).
- Jordan Lee has ONE open record: `clock_in` 09:00 local, `clock_out` null.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Open record clock_in | 09:00 local | UTC-stored |
| Clock-out time | 13:00 local | 4h raw span = 240 min |
| Break deduction | 0 min (this case) | Net worked = 240 min |
| Shift minimum | 360 min (6h) | |
| Expected total_work_minutes | 240 | |
| Expected overtime_minutes | null | |
| Expected status | SHORT_DAY | 240 < 360 minimum |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | While clocked in, click "Clock Out" at 13:00 local | `POST /api/v1/attendance/clock-out` returns 200 OK. |
| 2 | Inspect the response | `total_work_minutes: 240`, `overtime_minutes: null`, `status: "SHORT_DAY"`. |
| 3 | Verify the DB row | `total_work_minutes = 240`, `status = SHORT_DAY`, flagged for HR review (visible to HR review queues). |
| 4 | Observe the UI | Summary card shows total "4h 0m" with an amber "Short Day" pill. |
| 5 | Confirm it is review-flagged, not blocked | The clock-out still succeeds (the employee is not prevented from clocking out); the flag is informational for HR, per AC-4. |

## 6. Postconditions
- Record closed with `status = SHORT_DAY`, flagged for HR review; no overtime recorded.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
