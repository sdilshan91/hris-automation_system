---
id: TC-LV-055
user_story: US-LV-003
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-055: Half-day leave is created as 0.5 days and decrements balance accordingly

## 1. Test Objective
Verify that when an Employee toggles the half-day option and selects an AM or PM session, the resulting leave request is created for exactly 0.5 days and the projected/decremented balance reflects 0.5. (Test Hint: apply for a half-day and verify 0.5 day deduction.)

## 2. Related Requirements
- User Story: US-LV-003
- Acceptance Criteria: AC-4
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" is active; Employee "Jane Smith" is authenticated with `Leave.Apply`.
- Leave type "Annual Leave" is active with `half_day_allowed = true`.
- Jane has an Annual Leave balance of 5.0 days.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Jane Smith | Balance = 5.0 |
| Leave Type | Annual Leave | half_day_allowed = true |
| Start Date | 2026-07-06 (Mon) | Single working day |
| End Date | 2026-07-06 (Mon) | Same day |
| Is Half Day | true | -- |
| Half Day Session | AM | varchar(2) value "AM" |
| Expected total_days | 0.50 | numeric(5,2) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Leave Application page; select Annual Leave; Start = End = 2026-07-06 | Requested days chip shows 1.0 (full day) initially. |
| 2 | Toggle the Half-Day option on | An AM/PM session selector appears; the requested days chip updates to 0.5. |
| 3 | Select Session = AM | Projected remaining updates to 4.5 (5.0 - 0.5). |
| 4 | Submit the request via `POST /api/v1/leaves` with `isHalfDay:true`, `halfDaySession:"AM"` | Response 201 Created. |
| 5 | Verify response body | `is_half_day: true`, `half_day_session: "AM"`, `total_days: 0.50`, `status: "Pending"`. |
| 6 | Approve the request (or verify on approval) and check the leave balance | Balance decremented by exactly 0.5 (a `leave_ledger` "Used" entry of 0.50). |
| 7 | Attempt a half-day toggle for a multi-day range (Start != End) | Half-day is only permitted when Start == End; the UI disables half-day for multi-day ranges (or server rejects). |

## 6. Postconditions
- A `leave_request` exists with `total_days = 0.50`, `is_half_day = true`, `half_day_session = "AM"`.
- On approval, the balance decrements by exactly 0.5.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
