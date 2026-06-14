---
id: TC-ATT-104
user_story: US-ATT-008
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-104: Early clock-out with minimum hours already met -> NOT flagged as early departure (BR-2 carve-out, negative)

## 1. Test Objective
Verify the second condition of BR-2: early departure requires BOTH clock_out < shift_end AND the employee has NOT completed the shift's minimum required hours. If the employee leaves before the nominal end time but has already worked the minimum hours, the record is NOT flagged early. Worked example: a long-hours/early-start day where the employee has logged the 8h minimum by 16:30 and the shift ends at 17:00 -> `is_early_departure = false`, `early_departure_minutes = 0`.

## 2. Related Requirements
- User Story: US-ATT-008
- Acceptance Criteria: AC-3 (the not-flagged branch)
- Functional Requirements: FR-2 (clock-out comparison), FR-3 (flags persisted)
- Business Rules: BR-2 (early ONLY when minimum hours not met)

## 3. Preconditions
- Tenant "acme"; employee "Asha" with a SINGLE shift end_time 17:00, minimum_hours = 480 min (8h).
- Asha has an OPEN clock-in early enough that by 16:30 she has logged >= 8h net (e.g. clocked in 08:15, no/short break), so the minimum is met before the shift end.
- `Attendance.Clock.Self`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| shift end_time | 17:00 | nominal end |
| minimum_hours | 480 min (8h) | |
| clock-in time | 08:15 | early start |
| clock-out time | 16:30 | before end_time but >= 8h net worked |
| net minutes worked | >= 480 | minimum met |
| expected is_early_departure | false | BR-2 carve-out |
| expected early_departure_minutes | 0 | not flagged |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Asha, `POST /api/v1/attendance/clock-out` at 16:30 (net >= 8h) | 200; clock-out accepted. |
| 2 | Inspect the closed attendance_log | `is_early_departure = false`, `early_departure_minutes = 0` -- although 16:30 < 17:00, the minimum hours are met so BR-2 does not flag it. |
| 3 | Inspect the UI daily view | No "Early" badge appears (§8). |
| 4 | Contrast with TC-ATT-103 | The difference is solely the minimum-hours condition; clock-out before end_time alone is insufficient to flag early departure. |

## 6. Postconditions
- Asha's attendance_log is COMPLETE and NOT flagged early, tenant-scoped; the monthly early-departure count is unchanged.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- This is the direct counterpart to TC-ATT-103 and pins BR-2's two-condition logic (AND, not OR). **Reported to caller** if the backend flags early departure on clock_out < end_time alone (ignoring the minimum-hours carve-out) -- that would contradict BR-2.
