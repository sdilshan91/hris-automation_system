---
id: TC-ATT-055
user_story: US-ATT-005
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-055: Night shift (end_time < start_time) spans midnight -- saved as valid and duration computed across the day boundary (S10)

## 1. Test Objective
Verify the night-shift assumption (Section 10, Test Hint): a shift where `end_time` is earlier than `start_time` (e.g. 22:00 to 06:00) is a VALID shift, NOT a zero/negative-duration error -- the system interprets end_time as the next calendar day. Expected shift duration and the late-arrival / minimum-hours basis are computed across midnight correctly so that downstream clock-in/out calculations (US-ATT-001/002) span the day boundary.

## 2. Related Requirements
- User Story: US-ATT-005
- Functional Requirements: FR-1 (SINGLE), FR-2 (start/end)
- Assumptions/Constraints: S10 (night shift end < start = next day)
- Business Rules: BR-7 (zero-duration only; night shift is NOT zero-duration)

## 3. Preconditions
- Tenant "acme" `active`, Attendance module enabled.
- HR Officer authenticated with `Attendance.Shift.Manage`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| name | "Night Shift" | |
| type | SINGLE | |
| start_time | 22:00 | Day D |
| end_time | 06:00 | Day D+1 (next day) |
| break_duration_minutes | 30 | |
| working_days | [1,2,3,4,5] | Mon-Fri (the START day) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST .../shifts` with start 22:00 / end 06:00, type SINGLE | Response 201; the shift is created and is NOT rejected as zero/negative duration (end < start is the night-shift signal, not an error). |
| 2 | Inspect the computed nominal duration | The shift spans 22:00 -> 06:00 next day = 8h gross; net of the 30-min break = 7h30m. The system treats end_time as the next calendar day. |
| 3 | Resolve the shift for an employee assigned to it on a working day D | `GET .../employees/{employeeId}/shift?date=D` returns Night Shift; the expected work window is D 22:00 -> (D+1) 06:00 tenant-local. |
| 4 | Verify the late-threshold basis | The grace/late computation (consumed by US-ATT-008) anchors on the START datetime (D 22:00), so a clock-in at D+1 05:00 is NOT mistakenly flagged against a same-day end. |
| 5 | Boundary -- a 23:59 -> 23:59 same-value remains rejected | A genuine zero-duration (start == end) is still rejected (BR-7); only end < start is the valid night-shift case. |

## 6. Postconditions
- A valid night shift exists; duration and late basis are computed across midnight; integrates with US-ATT-001/002 clock calculations.

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
- The downstream clock-in/out span-midnight calculations live in US-ATT-001/002 (cf. TC-ATT-001/013). This TC verifies the SHIFT-DEFINITION side: a night shift is stored as valid and resolves with a correct cross-midnight work window. End-to-end clock calculations against a night shift integrate when seeded shift data from this story is available. **Reported to caller** as a cross-story integration seam.
