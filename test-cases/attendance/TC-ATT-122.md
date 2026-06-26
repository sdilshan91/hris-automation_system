---
id: TC-ATT-122
user_story: US-ATT-009
module: Attendance
priority: critical
type: functional
status: pass
created: 2026-06-15
---

# TC-ATT-122: Attendance period lock -- locking a period blocks clock-in/clock-out/regularization for the range, the lock is atomic + audited, and a duplicate/overlapping lock is rejected

## 1. Test Objective
Verify the Attendance Lock feature (AC-4, FR-3, FR-4, BR-1, NFR-2, NFR-4): `POST /api/v1/attendance/period-lock {periodStart, periodEnd}` freezes all attendance data for the range so no clock-in, clock-out, or regularization is permitted on those dates; the lock write is atomic and enforced by the `attendance_period_lock` table; the lock/unlock action is recorded in the audit log with the HR Officer's id + timestamp; and `GET /period-lock?month=` reflects the locked state.

## 2. Related Requirements
- User Story: US-ATT-009
- Acceptance Criteria: AC-4 (lock period -> all attendance records locked, no clock-in/out/regularization/modification; payroll can proceed)
- Functional Requirements: FR-3 (Attendance Lock freezes range, prevents modifications), FR-4 (log lock/unlock with HR id + timestamp)
- Business Rules: BR-1 (attendance must be locked before payroll finalize)
- Non-Functional: NFR-2 (lock atomic, DB-level constraint via locked_periods table), NFR-4 (no partial reads / data consistency)
- Data: §7 attendance_period_lock (lock_id, tenant_id, period_start, period_end, is_locked, locked_by, locked_at)
- API: POST /api/v1/attendance/period-lock; GET /period-lock?month=

## 3. Preconditions
- Tenant "acme"; HR Officer "Priya" authenticated.
- May 2026 attendance exists; period 2026-05-01..2026-05-31 not yet locked.
- Employee "Asha" has an open clock-in attempt and a regularization request targeting a May date for the negative checks.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| periodStart | 2026-05-01 | inclusive |
| periodEnd | 2026-05-31 | inclusive |
| locked_by | Priya (HR) | server-resolved actor |
| locked employee action date | 2026-05-15 | inside locked range |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya, `POST /period-lock {periodStart:2026-05-01, periodEnd:2026-05-31}` | 200/201; an attendance_period_lock row created with is_locked=true, locked_by=Priya, locked_at set, tenant_id=acme (server-stamped). |
| 2 | `GET /period-lock?month=2026-05` | Returns the lock with is_locked=true for the May range. |
| 3 | As Asha, attempt clock-in for 2026-05-15 (a date in the locked range) | Rejected -- attendance is locked for the period; no attendance_log written (AC-4/FR-3). |
| 4 | As Asha, attempt clock-out against a 2026-05-15 record | Rejected -- locked period; no modification applied. |
| 5 | As Asha, submit a regularization for 2026-05-15 | Rejected with the locked-period contract -- consistent with US-ATT-003 TC-ATT-029 exact string "This date falls within a locked payroll period. Please contact HR." |
| 6 | As a manager, attempt to approve a regularization that touches a 2026-05 date | Rejected -- approval cannot mutate attendance_log in a locked period (consistent with US-ATT-004 TC-ATT-045 / BR-5). |
| 7 | Verify audit log | A lock entry exists: actor=Priya, action=LOCK, period, timestamp (FR-4); the entry is immutable. |
| 8 | Atomicity: simulate failure mid-lock (e.g. forced error after row insert begins) | Either the lock is fully recorded or not at all -- no partial/half-locked state; no orphaned partial range (NFR-2). |
| 9 | Re-`POST /period-lock` for an OVERLAPPING range (e.g. 2026-05-10..2026-06-10) | Rejected/handled -- no duplicate or conflicting active lock for the same dates (range constraint, NFR-2). |
| 10 | Clock-in for a date OUTSIDE the locked range (e.g. 2026-06-02) | Succeeds -- the lock is scoped to its date range only. |

## 6. Postconditions
- The May 2026 period is locked; clock-in/out/regularization/approval on locked dates are blocked; the lock is audited and atomic; out-of-range dates remain editable; payroll may proceed (BR-1 precondition satisfied).

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- This story IMPLEMENTS the period-lock that US-ATT-003 (TC-ATT-029 submit) and US-ATT-004 (TC-ATT-045 approval) previously deferred as CONDITIONAL on the Payroll module. The lock-enforcement on clock-in/out/regularization is now verified live here; payroll CONSUMPTION of the lock-as-finalize-precondition (BR-1, payroll-finalize) remains PAYROLL-MODULE (TC-ATT-125 Step on cutoff; finalize is payroll-owned). **Reported to caller.**
- BR-1 "locked before finalize" -- the attendance side EXPOSES the lock state and blocks edits; the finalize gate itself lives in the Payroll run (not built). DEFERRED. **Reported to caller.**
- Tenant isolation of the lock (Tenant A cannot lock/read Tenant B's period) is covered by TC-ATT-ISO-012.
- NFR-2 names a "database-level constraint (e.g. locked_periods table with range checks)"; if the backend enforces overlap only at the application layer rather than a DB exclusion constraint, Step 9 still asserts the no-overlap behaviour and flags the mechanism. **Reported to caller.**
