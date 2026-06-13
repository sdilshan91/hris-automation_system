---
id: TC-LV-050
user_story: US-LV-003
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-050: Submission blocked when balance is insufficient and negative balance not allowed

## 1. Test Objective
Verify that when an Employee requests more days than their available balance for a leave type that does NOT allow negative balance, submission is blocked with a clear message and no `leave_request` is created. (Test Hint: apply for more days than available; verify rejection.)

## 2. Related Requirements
- User Story: US-LV-003
- Acceptance Criteria: AC-2
- Functional Requirements: FR-2
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" is active; Employee "Jane Smith" is authenticated with `Leave.Apply`.
- Leave type "Annual Leave" is active with `negative_balance_allowed = false`.
- Jane has an Annual Leave balance of 2.0 days for the current leave year.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Jane Smith | Annual Leave balance = 2.0 |
| Leave Type | Annual Leave | negative_balance_allowed = false |
| Start Date | 2026-07-06 (Mon) | -- |
| End Date | 2026-07-10 (Fri) | 5 working days requested |
| Requested days | 5.0 | Exceeds balance of 2.0 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Leave Application page and select Leave Type = "Annual Leave" | Balance panel shows "Available: 2.0 days". |
| 2 | Select Start Date = 2026-07-06, End Date = 2026-07-10 | Requested days chip shows 5.0; projected remaining shows -3.0 highlighted as a warning/error. |
| 3 | Observe the form state | The Submit button is disabled OR an inline error is shown: "Insufficient balance" with the shortfall. |
| 4 | If Submit is reachable, force the API call `POST /api/v1/leaves` with the 5-day range | Response is 400 Bad Request (or 422) with a clear message such as "Insufficient leave balance: you have 2.0 days available but requested 5.0." |
| 5 | Verify no `leave_request` row was created | No new row inserted; balance unchanged. |
| 6 | Reduce the End Date to 2026-07-07 (Tue) so requested = 2.0 | Submission is now permitted (balance exactly sufficient). |

## 6. Postconditions
- No `leave_request` is created for the over-balance request.
- The leave balance remains 2.0 days.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
