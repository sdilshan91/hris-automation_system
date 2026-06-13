---
id: TC-LV-057
user_story: US-LV-003
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-057: Maximum consecutive leave days enforced per leave type configuration

## 1. Test Objective
Verify that an Employee cannot submit a leave request that exceeds the `max_consecutive_days` configured on the leave type, that a request exactly at the limit is accepted, and that one day over the limit is rejected (boundary).

## 2. Related Requirements
- User Story: US-LV-003
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" is active; Employee "Jane Smith" is authenticated with `Leave.Apply`.
- Leave type "Casual Leave" is active with `max_consecutive_days = 3` (per US-LV-001 default seed).
- Jane has sufficient Casual Leave balance (>= 4 days).
- No public holiday falls within the test ranges.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Leave Type | Casual Leave | max_consecutive_days = 3 |
| Range at limit | 2026-07-06 (Mon) to 2026-07-08 (Wed) | 3 working days -> allowed |
| Range over limit | 2026-07-06 (Mon) to 2026-07-09 (Thu) | 4 working days -> rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Leave Application page; select Casual Leave; Start = 2026-07-06, End = 2026-07-08 (3 days) | Requested days chip shows 3.0; no error. |
| 2 | Submit the 3-day request | Response 201 Created -- exactly at the max consecutive limit is allowed. |
| 3 | Cancel that request to clear the overlap, then select Start = 2026-07-06, End = 2026-07-09 (4 working days) | Requested days chip shows 4.0; an inline error appears: "Casual Leave allows a maximum of 3 consecutive days." |
| 4 | Force submission of the 4-day request via `POST /api/v1/leaves` | Server returns 400/422 with the max-consecutive-days error. No request created. |
| 5 | Verify that the consecutive count is measured in working days, not calendar days | A range spanning a weekend that yields <= 3 working days is allowed even if calendar span > 3. |

## 6. Postconditions
- The at-limit (3-day) request is created with status "Pending".
- The over-limit (4-day) request is not created.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
