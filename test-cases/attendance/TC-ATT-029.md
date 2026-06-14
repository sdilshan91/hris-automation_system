---
id: TC-ATT-029
user_story: US-ATT-003
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-029: Regularization for a date inside a locked payroll period is rejected with the exact locked-period message (negative)

## 1. Test Objective
Verify that submitting a regularization for a date that falls within a LOCKED payroll period is rejected, no `attendance_regularization` row is created, and the system returns the exact message "This date falls within a locked payroll period. Please contact HR." Confirm the same date succeeds once the period is unlocked (BR-6 -- HR can unlock).

## 2. Related Requirements
- User Story: US-ATT-003
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7
- Business Rules: BR-6
- Dependencies: Payroll module (payroll period lock status)

## 3. Preconditions
- Tenant "acme", `active`, Attendance module enabled, regularization workflow configured.
- Lookback period is set wide enough (e.g., 35 days) that the target date is within the lookback window but inside a locked payroll period, so the locked-period check is the operative rejection (not the lookback check).
- A payroll period covering the target date exists with `locked = true`.
- Employee "Jordan Lee" is `active`, authenticated, holds `Attendance.Regularize.Self`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| date | a working day inside a locked payroll period | Within lookback, but locked |
| Payroll period lock | locked = true | The blocker |
| regularization_type | MISSED_BOTH | |
| reason | "Forgot to clock in for that day." | >= 10 chars |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Attempt to submit a regularization for the locked-period date via `POST /api/v1/attendance/regularizations` | Response status is 422 (or 409). No regularization row is created; no workflow initiated. |
| 2 | Inspect the error body / UI message | The message reads exactly: "This date falls within a locked payroll period. Please contact HR." |
| 3 | Verify the database | No `attendance_regularization` row exists for the target date. |
| 4 | Have HR unlock the payroll period (BR-6), then re-submit for the same date | With the period unlocked and the date within lookback, the submission SUCCEEDS (201) and a PENDING regularization is created. |
| 5 | Confirm the lock check precedence | When BOTH lookback and a locked period would block, document which check fires first per the implemented order; this TC isolates the locked-period path by keeping the date within lookback. (REPORTED TO CALLER: the payroll-period-lock integration depends on the Payroll module; if payroll lock state is not yet available, verify the error-contract surfaces and the unlocked/no-lock path passes, and re-run the locked-period assertion once Payroll lands.) |

## 6. Postconditions
- No regularization created while the period is locked; the exact locked-period message is shown.
- After HR unlocks, a regularization for the same date can be submitted.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
