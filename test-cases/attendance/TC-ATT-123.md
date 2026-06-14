---
id: TC-ATT-123
user_story: US-ATT-009
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-15
---

# TC-ATT-123: Unlock -> correct -> re-lock -- HR unlocks a locked period, the correction is allowed, re-locking restores the freeze; affected-payroll recalculation is signalled (payroll recompute DEFERRED)

## 1. Test Objective
Verify the unlock/re-lock cycle (AC-5, BR-6, FR-6): when attendance must be corrected after a payroll run has started but before finalization, HR unlocks the period (`POST /period-lock/{id}/unlock`), the previously-blocked modification is now allowed, HR re-locks the period, and the system signals that affected payroll entries must be recalculated. The attendance-side unlock->edit->re-lock and the recalculation SIGNAL are verified now; the payroll RECOMPUTATION is PAYROLL-MODULE and DEFERRED.

## 2. Related Requirements
- User Story: US-ATT-009
- Acceptance Criteria: AC-5 (unlock to correct an error, recalculate affected payroll entries, re-lock on confirm)
- Functional Requirements: FR-6 (trigger attendance refresh in payroll when records modified during an active run)
- Business Rules: BR-6 (if period unlocked after payroll started, affected payroll slips must be recalculated)
- Data: §7 attendance_period_lock (unlocked_by, unlocked_at)
- API: POST /period-lock/{id}/unlock; POST /period-lock; GET /period-lock?month=

## 3. Preconditions
- Tenant "acme"; period 2026-05 LOCKED (TC-ATT-122) by HR "Priya".
- A payroll run for 2026-05 has been initiated (or is modelled as started) so BR-6 recalculation applies.
- A known correction is needed: Asha's 2026-05-15 record must be regularized.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| lock_id | (from TC-ATT-122) | the active May lock |
| unlocked_by | Priya (HR) | server-resolved |
| correction | regularize Asha 2026-05-15 | the edit blocked while locked |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya, `POST /period-lock/{lock_id}/unlock` | 200; the lock row is_locked=false (or status=unlocked), unlocked_by=Priya, unlocked_at set; audit entry action=UNLOCK with actor+timestamp (FR-4). |
| 2 | `GET /period-lock?month=2026-05` | Reflects the unlocked state. |
| 3 | Perform the correction blocked in TC-ATT-122 (regularize/approve Asha 2026-05-15) | Now ALLOWED -- the modification succeeds while the period is unlocked (AC-5). |
| 4 | Verify a recalculation SIGNAL is raised for the affected employee/period | A "payroll recalculation needed" marker/seam is produced (referencing the affected employee + 2026-05) so the payroll run knows to re-pull (FR-6/BR-6) -- the SIGNAL is verified; the actual payroll slip recompute is DEFERRED on the Payroll module. |
| 5 | Re-pull `payroll-data?month=2026-05&employeeIds=Asha` after the correction | Returns the UPDATED inputs reflecting the correction (the corrected day is now present/regularized) -- payroll's "Refresh from Attendance" gets fresh data. |
| 6 | As Priya, `POST /period-lock` to RE-LOCK 2026-05 (on HR confirm) | The period is locked again; further edits blocked (as TC-ATT-122); re-lock audited. |
| 7 | After re-lock, retry the correction | Blocked again -- the freeze is restored. |
| 8 | Negative: a non-HR user attempts unlock | 403 -- unlock is HR-only (authz cross-ref TC-ATT-127). |

## 6. Postconditions
- The period was unlocked, corrected, and re-locked; unlock/lock actions are audited; the corrected data is reflected in a fresh payroll-data pull; a recalculation signal was raised for the affected payroll entries. Payroll slip recompute remains DEFERRED.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Payroll recalculation (AC-5 "recalculates affected payroll entries" / BR-6 / FR-6) is PAYROLL-MODULE and DEFERRED** -- the Payroll module is not built. The attendance side verified here: unlock allows the edit, raises the recalculation SIGNAL/seam, and serves fresh payroll-data on re-pull. The actual payroll-slip recomputation is exercised under the Payroll suite. Consistent with the deferred payroll-consumption pattern across US-ATT-006/007/008. **Reported to caller.**
- FR-6 "trigger attendance refresh in payroll during an active run": the attendance-side change-signal + fresh-data pull is verified; the "Refresh from Attendance" button on the payroll run page (§8) is a PAYROLL UI element -- DEFERRED. **Reported to caller.**
