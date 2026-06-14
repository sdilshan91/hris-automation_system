---
id: TC-ATT-059
user_story: US-ATT-005
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-059: Rotating shift -- define a cyclic rotation pattern and verify the correct shift resolves for dates across the full cycle (AC-5, FR-1, FR-7)

## 1. Test Objective
Verify the rotating-shift behavior (AC-5, FR-1, FR-7, S10): HR defines a ROTATING shift with a rotation pattern (e.g. Week A: Morning, Week B: Evening) and a reference start date, assigns it to employees, and the system automatically determines the applicable shift for ANY given date by computing the position in the cycle. Resolution is correct at the first day, mid-week, the week-boundary roll-over, and after the cycle repeats.

## 2. Related Requirements
- User Story: US-ATT-005
- Acceptance Criteria: AC-5
- Functional Requirements: FR-1 (ROTATING type), FR-7 (store pattern; calculate applicable shift for any date)
- Assumptions/Constraints: S10 (rotation cycles indefinitely from a reference start date)

## 3. Preconditions
- Tenant "acme" `active`, Attendance module enabled.
- HR Officer authenticated with `Attendance.Shift.Manage`.
- Two component shifts exist: "Morning" (06:00-14:00) and "Evening" (14:00-22:00).
- Reference cycle start date = a known Monday, R.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Rotation | 2-week cycle: Week A = Morning, Week B = Evening | |
| Reference start | Monday R | Cycle anchor |
| Assigned employee | E1 | effective_from = R |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Define the ROTATING shift with the 2-week pattern and reference start R; assign E1 effective R | Response 201/200; the rotation pattern is stored and E1 is assigned. |
| 2 | `GET .../employees/E1/shift?date=R` (Week A, day 1) | Resolves to "Morning". |
| 3 | `GET .../employees/E1/shift?date=R+3` (mid Week A) | Resolves to "Morning". |
| 4 | `GET .../employees/E1/shift?date=R+7` (start of Week B) | Resolves to "Evening" (week-boundary roll-over correct). |
| 5 | `GET .../employees/E1/shift?date=R+10` (mid Week B) | Resolves to "Evening". |
| 6 | `GET .../employees/E1/shift?date=R+14` (cycle repeats -> Week A again) | Resolves to "Morning" (the cycle repeats indefinitely from R). |
| 7 | Resolve a date BEFORE the reference start R | Returns no rotation shift for E1 on that date (falls back to a prior assignment or the tenant default per FR-5); the rotation does not apply before its anchor. |
| 8 | Verify the weekly rotation view | The UI weekly view (UI/UX S8) renders the pattern consistently with the resolver output for the displayed dates. |

## 6. Postconditions
- The rotation pattern is stored; the applicable shift resolves correctly for any date across and beyond the cycle.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
