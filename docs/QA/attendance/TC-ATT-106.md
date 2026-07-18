---
id: TC-ATT-106
user_story: US-ATT-008
module: Attendance
priority: high
type: functional
status: pass
created: 2026-06-14
---

# TC-ATT-106: Grace-period resolution hierarchy -- shift-level grace -> tenant default -> 0 when neither set (BR-3, boundary)

## 1. Test Objective
Verify BR-3's grace-period fallback chain used by the late-detection comparison: grace is taken from the shift if set; otherwise the tenant-level default applies; otherwise it is 0 (any clock-in after start is late). Confirms FR-1 reads grace from the correct source in each case.

## 2. Related Requirements
- User Story: US-ATT-008
- Functional Requirements: FR-1 (start + grace comparison)
- Business Rules: BR-3 (grace from shift, else tenant default, else 0)
- Dependency: US-ATT-005 (grace_period defined at shift level)

## 3. Preconditions
- Tenant "acme"; a tenant-level default grace is configured (e.g. 10 min).
- Three SINGLE shifts on a 09:00 start: Shift-X grace = 15 (shift-level set); Shift-Y grace = null (falls back to tenant default 10); Shift-Z grace = null with the tenant default ALSO cleared (falls back to 0).
- Three employees A/B/C assigned to X/Y/Z respectively; `Attendance.CheckIn`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| tenant default grace | 10 min | applies when shift grace null |
| Shift-X grace | 15 min | shift-level wins |
| Shift-Y grace | null | -> tenant default 10 |
| Shift-Z grace | null (+ tenant default cleared) | -> 0 |
| clock-in (all) | 09:08 | 8 min after start |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Employee A (Shift-X, grace 15) clocks in 09:08 | `is_late = false` -- within the 15-min shift-level grace. |
| 2 | Employee B (Shift-Y, grace null) clocks in 09:08 | `is_late = false` -- within the tenant default 10-min grace (BR-3 fallback to tenant default). |
| 3 | Employee C (Shift-Z, grace null + tenant default cleared) clocks in 09:08 | `is_late = true`, `late_minutes = 8` -- grace resolves to 0, so any clock-in after 09:00 is late (BR-3 final fallback). |
| 4 | Cross-check the resolved grace per employee | Each detection used the grace from the expected source (shift > tenant > 0). |

## 6. Postconditions
- Three attendance_logs demonstrating the grace resolution hierarchy; all tenant-scoped to acme.

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
- The tenant-level default grace storage location (late_policy vs shift/tenant settings) is an implementation detail; this TC asserts the resolved BEHAVIOUR per BR-3, not the storage field. **Reported to caller** if a tenant-default grace field does not exist yet -- then only the shift-level (Step 1) and zero-fallback (Step 3) branches are verifiable, and the tenant-default branch (Step 2) is CONDITIONAL on that configuration surface.
