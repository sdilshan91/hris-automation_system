---
id: TC-ATT-102
user_story: US-ATT-008
module: Attendance
priority: high
type: functional
status: pass
created: 2026-06-14
---

# TC-ATT-102: Grace-cutoff boundary -- clock-in exactly at start+grace (09:15) is on-time; one minute past (09:16) is late by 1 (boundary)

## 1. Test Objective
Verify the exact grace boundary semantics of BR-1/FR-1: late = clock_in > (start + grace), so the cutoff instant itself is on-time and the next minute is late. Worked example on a 09:00 shift with 15-min grace (cutoff 09:15): clock-in at exactly 09:15 -> not late; clock-in at 09:16 -> `is_late = true`, `late_minutes = 16`, `late_by = 1`. Pins the inclusive/exclusive edge so off-by-one regressions are caught.

## 2. Related Requirements
- User Story: US-ATT-008
- Acceptance Criteria: AC-1 (late side), AC-2 (on-time side)
- Functional Requirements: FR-1 (start + grace comparison)
- Business Rules: BR-1 (strict greater-than -- equality is on-time)

## 3. Preconditions
- Tenant "acme"; employee "Asha" with a SINGLE shift start 09:00, grace 15 min, working weekday; `Attendance.Clock.Self`.
- Two independent test runs (or two employees) so each boundary punch is the first clock-in of its day.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| shift start_time | 09:00 | |
| grace cutoff | 09:15 | start + 15 |
| case A clock-in | 09:15 | exactly at cutoff |
| case B clock-in | 09:16 | one minute past |
| expected A | is_late=false, late_minutes=0 | equality is on-time (BR-1 strict >) |
| expected B | is_late=true, late_minutes=16, late_by=1 | first minute over the cutoff |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Case A: clock-in at exactly 09:15:00 | 200; attendance_log `is_late = false`, `late_minutes = 0` -- the cutoff instant is on-time (BR-1 uses strict `>`). No late badge. |
| 2 | Case B: clock-in at 09:16:00 (fresh day/employee) | 200; attendance_log `is_late = true`, `late_minutes = 16` (from start), `late_by = 1` (from cutoff). Amber "Late by 16 min" badge. |
| 3 | Compare the two records | The only difference is a single minute crossing the cutoff -- confirms the boundary is exactly at start+grace and exclusive of equality. |
| 4 | Sub-minute check (if seconds are tracked) | Confirm whether 09:15:30 is on-time or late, i.e. whether the comparison truncates to whole minutes or compares timestamps -- record the observed behaviour (see Notes). |

## 6. Postconditions
- Two attendance_logs demonstrating the on-time (09:15) and late (09:16) sides of the grace boundary, tenant-scoped to acme.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Whole-minute vs timestamp comparison.** This TC assumes BR-1 compares to whole-minute resolution (09:15:00 = on-time, 09:16:00 = late). If the backend compares raw timestamps, a 09:15:30 punch would already be > 09:15:00 and counted late. Step 4 records the observed behaviour. **Reported to caller** -- confirm the rounding/truncation rule in the `AttendanceCalculator`.
- The on-time equality side (AC-2) reuses the same code path as TC-ATT-100; this TC isolates the one-minute boundary specifically.
