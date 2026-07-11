---
id: TC-ATT-103
user_story: US-ATT-008
module: Attendance
priority: critical
type: functional
status: pass
created: 2026-06-14
---

# TC-ATT-103: Early departure -- 17:00 shift end, clock-out 16:30, minimum hours NOT met -> is_early_departure=true, early_departure_minutes=30 (happy path)

## 1. Test Objective
Verify AC-3/FR-2/FR-3/BR-2: when an employee clocks out before the shift end time AND has not completed the shift's minimum required hours, the record is marked Early Departure. Worked example: shift ends 17:00, clock-out at 16:30 with minimum hours not met -> `is_early_departure = true`, `early_departure_minutes = 30`. Detection runs inline in the clock-out transaction (NFR-1). Grace does NOT apply to early departure (S10).

## 2. Related Requirements
- User Story: US-ATT-008
- Acceptance Criteria: AC-3
- Functional Requirements: FR-2 (compare clock-out vs shift end), FR-3 (persist is_early_departure, early_departure_minutes)
- Non-Functional: NFR-1 (inline detection)
- Business Rules: BR-2 (early = clock_out < end AND minimum hours not met)
- Assumptions: S10 (grace applies only to late arrival, not early departure)

## 3. Preconditions
- Tenant "acme"; employee "Asha" with a SINGLE shift end_time 17:00, minimum_hours sufficient that a 16:30 departure leaves it unmet (e.g. 8h with an on-time 09:00 start -> ~7.5h worked < 8h).
- Asha has an OPEN clock-in for today (on-time start); `Attendance.Clock.Self`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| shift end_time | 17:00 | |
| clock-out time | 16:30 | 30 min before end |
| minimum_hours | 480 min (8h) | not met at 16:30 |
| expected is_early_departure | true | AC-3, BR-2 |
| expected early_departure_minutes | 30 | 17:00 - 16:30 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Asha, `POST /api/v1/attendance/clock-out` at 16:30 | 200; clock-out accepted (early departure is recorded, not blocked). |
| 2 | Inspect the closed attendance_log | `is_early_departure = true`, `early_departure_minutes = 30`; tenant_id = acme. |
| 3 | Confirm minimum-hours condition (BR-2) | The flag is set because BOTH conditions hold -- clock-out < 17:00 AND minimum hours not completed (the min-hours-met carve-out is asserted in TC-ATT-104). |
| 4 | Confirm grace does not apply (S10) | No early-departure grace window is applied -- 16:59 would still be early by 1; only late arrival uses grace. |
| 5 | Inspect the UI daily view | An "Early" badge (e.g. "Left 30 min early") appears next to the clock-out time (§8). |

## 6. Postconditions
- Asha's attendance_log is flagged early departure (is_early_departure=true, early_departure_minutes=30), tenant-scoped; the monthly early-departure count is incremented.

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
- early_departure_minutes is measured against the shift END time (17:00 - 16:30 = 30), not against minimum hours. BR-2 uses minimum-hours-not-met only as a GATE for whether to flag at all (see TC-ATT-104). **Reported to caller** if the backend instead measures the minutes shortfall against minimum hours rather than the shift end.
