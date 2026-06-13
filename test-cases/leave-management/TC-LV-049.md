---
id: TC-LV-049
user_story: US-LV-003
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-049: Real-time balance display on leave type and date selection

## 1. Test Objective
Verify that when an Employee selects a leave type and a date range on the application form, the system displays the current available balance, the requested day count, and the projected remaining balance inline before submission (FR-2 / AC-2 inline balance display).

## 2. Related Requirements
- User Story: US-LV-003
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2, FR-3

## 3. Preconditions
- Tenant "acme" is active; Employee "Jane Smith" is authenticated with `Leave.Apply`.
- Jane has an Annual Leave balance of exactly 10.0 days for the current leave year.
- Leave type "Annual Leave" is active.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Jane Smith | Annual Leave balance = 10.0 |
| Leave Type | Annual Leave | Active |
| Start Date | 2026-07-06 (Mon) | No holiday in range |
| End Date | 2026-07-10 (Fri) | 5 working days |
| Expected requested days | 5.0 | Mon-Fri |
| Expected projected remaining | 5.0 | 10.0 - 5.0 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Leave Application page | Form loads; balance panel is empty/placeholder until a leave type is selected. |
| 2 | Select Leave Type = "Annual Leave" | The balance panel shows "Available: 10.0 days" pulled from the balance source (cache, fallback to DB per NFR-2). |
| 3 | Select Start Date = 2026-07-06, End Date = 2026-07-10 | "Requested: 5.0 days" chip appears; "Projected remaining: 5.0 days" is displayed. |
| 4 | Change End Date to 2026-07-08 (Wed) | Requested days updates to 3.0; projected remaining updates to 7.0 in real time without page reload. |
| 5 | Toggle Half Day on with AM session for a single day | Requested days updates to 0.5; projected remaining updates to 9.5. |
| 6 | Verify all values are consistent | available - requested = projected remaining at every change. |

## 6. Postconditions
- No leave request is created (read-only preview).
- Balance values displayed match the authoritative balance source.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
