---
id: TC-ATT-105
user_story: US-ATT-008
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-105: Flexible shift exemption -- clock-in/out at any time triggers NO late or early-departure flag; only minimum hours enforced (BR-6, §10, negative)

## 1. Test Objective
Verify BR-6 and §10: employees on FLEXIBLE shifts (no fixed start/end) are not subject to late-arrival or early-departure tracking regardless of clock times -- only the minimum-hours rule applies. Worked example: a FLEXIBLE-shift employee clocks in at 11:00 and out at 15:00; no late flag and no early-departure flag is set, even though those times would be late/early on a fixed shift.

## 2. Related Requirements
- User Story: US-ATT-008
- Functional Requirements: FR-1/FR-2 (do not apply to flexible shifts)
- Business Rules: BR-6 (flexible shifts -> no late/early tracking; only minimum hours)
- Assumptions: S10 (late/early tracking applies only to SINGLE and ROTATING shifts, not FLEXIBLE)

## 3. Preconditions
- Tenant "acme"; employee "Finn" assigned a FLEXIBLE shift (minimum_hours = 360 min / 6h, no start_time/end_time -- per US-ATT-005 TC-ATT-054).
- `Attendance.Clock.Self`; no open clock-in for today.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| shift type | FLEXIBLE | no start/end (US-ATT-005) |
| minimum_hours | 360 min (6h) | only rule enforced |
| clock-in time | 11:00 | would be "late" on a fixed shift |
| clock-out time | 15:00 | would be "early" on a fixed shift |
| expected is_late | false | BR-6 exemption |
| expected is_early_departure | false | BR-6 exemption |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Finn, `POST /api/v1/attendance/clock-in` at 11:00 | 200; attendance_log created with `is_late = false`, `late_minutes = 0` -- no late evaluation for FLEXIBLE shifts (BR-6). |
| 2 | As Finn, `POST /api/v1/attendance/clock-out` at 15:00 | 200; closed log has `is_early_departure = false`, `early_departure_minutes = 0` -- no early-departure evaluation (BR-6). |
| 3 | Confirm minimum-hours rule still applies | If net worked (4h) < minimum_hours (6h), the day is treated as short per US-ATT-002 (TC-ATT-017), but it is NOT a late/early-departure flag -- the two concerns are distinct. |
| 4 | Inspect the UI daily view | No "Late"/"Early" badges for the FLEXIBLE-shift day (§8). |
| 5 | Confirm late/early report exclusion | Finn does not appear with late/early counts in the late/early report (TC-ATT-112) for these punches. |

## 6. Postconditions
- Finn's FLEXIBLE-shift attendance_log carries no late/early flags; only the minimum-hours/short-day concern (owned by US-ATT-002) applies.

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
- BR-6/§10 scope late/early tracking to SINGLE and ROTATING shifts only. ROTATING shifts resolve a per-day applicable shift (US-ATT-005 TC-ATT-059) whose start/end then drive late/early the same as SINGLE -- not separately re-tested here. **Reported to caller** if the detector evaluates FLEXIBLE shifts (e.g. against a 00:00 default start) -- that would violate BR-6.
