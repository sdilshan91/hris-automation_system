---
id: TC-ATT-100
user_story: US-ATT-008
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-100: On-time clock-in within grace -- 09:00 shift, 15-min grace, clock-in 09:10 -> not late, no late flag set (happy path)

## 1. Test Objective
Verify AC-2/FR-1/BR-1/BR-3: when an employee clocks in after the shift start time but within the grace window, the clock-in is accepted as on-time and NO late flag is set. Worked example: shift starts 09:00 with a 15-minute grace period; clock-in at 09:10 is within grace, so `is_late = false` and `late_minutes = 0`. Detection runs inline in the clock-in transaction (NFR-1).

## 2. Related Requirements
- User Story: US-ATT-008
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1 (compare clock-in vs shift start + grace), FR-3 (persist is_late/late_minutes on attendance_log)
- Non-Functional: NFR-1 (computed inline in the clock-in transaction)
- Business Rules: BR-1 (late = clock_in > start + grace), BR-3 (grace from shift, else tenant default, else 0)

## 3. Preconditions
- Tenant "acme" active, Attendance module enabled.
- Employee "Asha" authenticated with `Attendance.Clock.Self`, assigned a SINGLE shift with start_time 09:00, grace_period_minutes = 15, on a working weekday.
- Asha has no open clock-in for today.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| shift start_time | 09:00 | SINGLE shift |
| grace_period_minutes | 15 | shift-level (BR-3) |
| clock-in time | 09:10 | within grace window (09:00..09:15) |
| expected is_late | false | within grace |
| expected late_minutes | 0 | AC-2 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Asha, `POST /api/v1/attendance/clock-in` at 09:10 (local) | 200; clock-in accepted; the attendance_log is created with the recorded clock-in time. |
| 2 | Inspect the created attendance_log | `is_late = false`, `late_minutes = 0`; no separate late-detection API call is made (NFR-1 -- computed in the clock-in transaction). |
| 3 | Inspect the UI daily attendance view | No "Late" badge is shown next to the clock-in time (§8). |
| 4 | Confirm grace boundary semantics | A clock-in anywhere in 09:00..09:15 inclusive is on-time (the one-minute-past-grace case is asserted in TC-ATT-102). |

## 6. Postconditions
- Asha has an OPEN attendance_log for today flagged on-time (is_late=false, late_minutes=0), tenant-scoped to acme.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- Complements TC-ATT-006 (US-ATT-001) which exercised the grace boundary at clock-in time; US-ATT-008 owns the end-to-end is_late/late_minutes persistence on the attendance_log (FR-3), DEFERRED there to this story per the US-ATT-005 TC-ATT-062 note.
