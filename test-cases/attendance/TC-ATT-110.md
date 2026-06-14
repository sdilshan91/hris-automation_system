---
id: TC-ATT-110
user_story: US-ATT-008
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-110: Regularized attendance recompute -- regularizing a late clock-in to an on-time value clears/recalculates the late flag from the regularized times (BR-7)

## 1. Test Objective
Verify BR-7: late/early status is derived from the REGULARIZED times, not the original or submission times. When a manager-approved regularization changes a late clock-in to an on-time value (within grace), the attendance_log's late flag is recalculated -- `is_late` clears and `late_minutes` resets to 0. The converse (regularizing an on-time record to a late one) sets the flag.

## 2. Related Requirements
- User Story: US-ATT-008
- Functional Requirements: FR-1 (late comparison applied to regularized time), FR-3 (flags persisted)
- Business Rules: BR-7 (regularized records inherit late/early status from regularized times)
- Dependency: US-ATT-003 (regularization submit), US-ATT-004 (approval -> attendance_log update)

## 3. Preconditions
- Tenant "acme"; employee "Asha" on a 09:00 SINGLE shift, 15-min grace (cutoff 09:15).
- Asha has an attendance_log for a past in-lookback date with clock-in 09:30 -> currently is_late = true, late_minutes = 30.
- A manager "Mark" with approval rights over Asha (US-ATT-004); a regularization correcting the clock-in to 09:05.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| original clock-in | 09:30 | is_late=true, late_minutes=30 |
| grace cutoff | 09:15 | |
| regularized clock-in | 09:05 | within grace -> on-time |
| expected after approval | is_late=false, late_minutes=0 | recomputed from regularized time (BR-7) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm the pre-regularization state | attendance_log: clock-in 09:30, `is_late = true`, `late_minutes = 30`. |
| 2 | Asha submits a regularization correcting clock-in to 09:05 (US-ATT-003); Mark approves (US-ATT-004) | On approval the attendance_log is updated with clock-in 09:05 and the late detection is re-run against the regularized time. |
| 3 | Inspect the updated attendance_log | `is_late = false`, `late_minutes = 0` -- recalculated from the regularized 09:05 (within grace), per BR-7. The UI late badge is removed. |
| 4 | Verify the monthly count adjusts | Asha's monthly late count decrements by 1 (the cleared late no longer counts toward deduction/chronic thresholds in TC-ATT-107/108). |
| 5 | Converse case | Regularizing an on-time 09:05 record to a late 09:40 sets `is_late = true`, `late_minutes = 40` -- the recompute is symmetric and uses the regularized time, not the submission time. |

## 6. Postconditions
- Asha's attendance_log late status reflects the regularized times; the monthly aggregate is consistent with the recomputed flags; all tenant-scoped to acme.

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
- BR-7 explicitly says the late/early status uses the regularized times, NOT the submission time -- this TC pins both directions (late->on-time clears, on-time->late sets). The approval-time attendance_log update mechanism is owned by US-ATT-004 (TC-ATT-037/044); this TC adds the late/early RECOMPUTE on that update. **Reported to caller** if the recompute does not run on regularization approval (i.e. the late flag is stale after times change) -- that would violate BR-7.
- Early-departure recompute on a clock-out regularization follows the same rule (BR-2 re-evaluated against the regularized clock-out + minimum hours).
