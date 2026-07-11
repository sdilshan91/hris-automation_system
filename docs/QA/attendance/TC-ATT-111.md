---
id: TC-ATT-111
user_story: US-ATT-008
module: Attendance
priority: medium
type: functional
status: fail
created: 2026-06-14
---

# TC-ATT-111: Half-day leave -- late/early evaluated against the half-day schedule, not the full-shift times (BR-8)

## 1. Test Objective
Verify BR-8: when an employee is on approved half-day leave, late-arrival and early-departure are evaluated against the half-day schedule (the working half's adjusted start/end), not the full shift. Worked example: a 09:00-17:00 shift with an approved first-half leave (afternoon working, e.g. 13:00 start) -> a 13:05 clock-in is evaluated against the 13:00 half-day start (within grace = on-time), NOT flagged late against 09:00.

## 2. Related Requirements
- User Story: US-ATT-008
- Functional Requirements: FR-1/FR-2 (comparison against the applicable schedule)
- Business Rules: BR-8 (half-day leave -> evaluated against a half-day shift schedule)
- Dependency: Leave Management (approved half-day leave), US-ATT-005 (shift), US-ATT-007 (half-day present accounting BR-5)

## 3. Preconditions
- Tenant "acme"; employee "Asha" on a 09:00-17:00 SINGLE shift, 15-min grace, minimum_hours 8h.
- Asha has an APPROVED first-half (morning) leave for the target date, so the working half is the afternoon (half-day start 13:00, half-day end 17:00, half-day minimum 4h).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| full shift | 09:00-17:00 | nominal |
| approved leave | first half (morning) | working half = afternoon |
| half-day start | 13:00 | derived working-half start |
| grace | 15 min | cutoff 13:15 |
| clock-in | 13:05 | within half-day grace -> on-time |
| clock-out | 17:00 | meets half-day schedule |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | With approved morning half-day leave, Asha clocks in at 13:05 | `is_late = false` -- evaluated against the 13:00 half-day start (within grace), NOT against 09:00 (BR-8). |
| 2 | Asha clocks out at 17:00 | `is_early_departure = false` -- the afternoon half-day schedule is met. |
| 3 | Negative control | A 13:25 clock-in (past the 13:15 half-day grace cutoff) IS flagged late_minutes = 25 against the half-day start -- confirming the half-day schedule (not the full shift) is the reference. |
| 4 | Confirm half-day present accounting | The day is treated per US-ATT-007 BR-5 (0.5 present) -- the late/early evaluation here is independent of, and complements, that accounting. |

## 6. Postconditions
- Asha's half-day attendance_log carries late/early flags computed against the half-day schedule, tenant-scoped to acme.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Half-day schedule derivation DEPENDS on Leave Management + the half-day policy.** How the half-day start/end is derived (first-half vs second-half leave -> which half is working, and the split time) depends on the approved leave's half indicator and the tenant's half-day split definition. This TC assumes a first-half leave yields an afternoon working half starting at the configured split (13:00). **Reported to caller** -- confirm the half-day schedule source and the split rule; if the half-day schedule is not yet derivable in the detector, the half-day branch is CONDITIONAL on that integration. Lower priority (medium) given the dependency.
