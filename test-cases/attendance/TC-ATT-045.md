---
id: TC-ATT-045
user_story: US-ATT-004
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-045: Approving a regularization whose date is in a now-locked payroll period is blocked with a contact-HR message (CONDITIONAL on Payroll)

## 1. Test Objective
Verify BR-5: if the regularized date falls within a payroll period that has been locked since the request was submitted, the approval is blocked at decision time with a message directing the manager to contact HR; no attendance_log is created/updated and the request stays PENDING. This complements US-ATT-003 TC-ATT-029, which blocks submission into an already-locked period; this TC blocks APPROVAL when the lock occurred after submission. CONDITIONAL on the Payroll module exposing period-lock state.

## 2. Related Requirements
- User Story: US-ATT-004
- Business Rule: BR-5 (approval blocked if the date is in a locked payroll period -- contact HR)
- Dependency: Payroll module (US-ATT-009 / payroll period lock)

## 3. Preconditions
- Tenant "acme", manager "Dana Wells" authenticated with `Attendance.Approve.Team`.
- A PENDING `attendance_regularization` exists for Dana's direct report Jordan for a date that was UNLOCKED at submission.
- The payroll period covering that date is subsequently LOCKED.
- **CONDITIONAL:** requires Payroll to expose lock state. If unavailable, verify the unlocked-approval path live and record the locked-path assertion as CONDITIONAL/DEFERRED.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| regularization_id | Jordan's PENDING request | date in a now-locked period |
| Expected message | a contact-HR message, e.g. "This date falls within a locked payroll period. Please contact HR." | match the implemented BR-5 string (align with TC-ATT-029) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Lock the payroll period covering the request's date | The period is now locked (CONDITIONAL on Payroll). |
| 2 | As Dana, `POST .../{regularization_id}/approve` | Request is blocked (409/422) with the contact-HR message; status stays PENDING. |
| 3 | Inspect attendance_log | No attendance_log is created/updated -- the locked period is protected. |
| 4 | Unlocked control (live) | For a request whose date is in an UNLOCKED period, approval succeeds and writes the attendance_log (the live-verifiable path now). |

## 6. Postconditions
- Approval into a locked payroll period is blocked with a contact-HR message and no attendance mutation; the unlocked path approves normally.

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
- **CONDITIONAL on the Payroll module.** The unlocked-approval path and the exact error-contract are verified now; the locked-period assertion activates once Payroll exposes period-lock state. Consistent with US-ATT-003 TC-ATT-029 and the leave-management payroll-lock deferrals. **Reported to caller.**
