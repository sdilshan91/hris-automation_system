---
id: TC-ATT-057
user_story: US-ATT-005
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-057: Effective-dated reassignment with no overlap -- Shift A active now, Shift B activates on its future effective date; only one active at a time (AC-3, FR-4, BR-2, BR-3)

## 1. Test Objective
Verify effective-dated shift history and the single-active-shift invariant (AC-3, FR-4, BR-2, BR-3): an employee currently on Shift A is reassigned to Shift B with a FUTURE effective date; Shift A remains active until that date (its effective_to is set to the day before B's effective_from, or A is closed at B's start), Shift B becomes active on its effective_from, and at no point do two active assignments overlap. Resolution by date returns exactly one shift for any given day.

## 2. Related Requirements
- User Story: US-ATT-005
- Acceptance Criteria: AC-3
- Functional Requirements: FR-4 (effective_from/effective_to history)
- Business Rules: BR-2 (one active shift at any time), BR-3 (assignments effective-dated, apply from effective_from)

## 3. Preconditions
- Tenant "acme" `active`, Attendance module enabled.
- HR Officer authenticated with `Attendance.Shift.Manage`.
- Employee E1 currently assigned to Shift A (effective_from <= today, effective_to null).
- Shift B exists. Reference "next Monday" = a date strictly in the future.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Current assignment | E1 -> Shift A (effective_to null) | Active now |
| New assignment | E1 -> Shift B, effectiveFrom = next Monday | Future-dated |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `POST .../shifts/{ShiftB_id}/assign` with `{ employeeIds:[E1], effectiveFrom: nextMonday }` | Response 200; a new employee_shift row for E1 -> Shift B with effective_from = next Monday. |
| 2 | Inspect Shift A's row for E1 | Shift A's effective_to is set so it ends the day before next Monday (no overlap with B); A is NOT deleted -- history is preserved. |
| 3 | `GET .../employees/E1/shift?date=today` | Returns Shift A (B has not started yet). |
| 4 | `GET .../employees/E1/shift?date=(nextMonday - 1 day)` | Returns Shift A (last active day of A). |
| 5 | `GET .../employees/E1/shift?date=nextMonday` | Returns Shift B (B activates exactly on its effective_from). |
| 6 | Verify the no-overlap invariant (BR-2) | For E1, no two employee_shift rows have overlapping [effective_from, effective_to] intervals; querying any single date yields exactly one active shift. |
| 7 | Negative -- attempt to create a second CURRENT assignment overlapping today | Rejected (or the existing current row is closed) per BR-2; the system never leaves two simultaneously-active rows for the same employee. |

## 6. Postconditions
- E1 has a contiguous, non-overlapping shift history: Shift A through (next Monday - 1), Shift B from next Monday; exactly one active per date.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
