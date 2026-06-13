---
id: TC-LV-059
user_story: US-LV-003
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-059: Probation employee can only see and apply for probation-eligible leave types

## 1. Test Objective
Verify that an employee on probation is only shown leave types marked `probation_eligible` in the application dropdown, and that the API rejects a submission for a non-probation-eligible leave type by a probation employee.

## 2. Related Requirements
- User Story: US-LV-003
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" is active.
- Employee "Tom Junior" has status Probation and is authenticated with `Leave.Apply`.
- Leave type "Sick Leave" is active with `probation_eligible = true`.
- Leave type "Annual Leave" is active with `probation_eligible = false`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee Tom Junior | status = Probation | -- |
| Sick Leave | probation_eligible = true | Visible to probation |
| Annual Leave | probation_eligible = false | Hidden from probation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Tom Junior (Probation); open the Leave Application page | The dropdown shows Sick Leave but NOT Annual Leave. |
| 2 | Inspect the eligible-types API response for Tom | Non-probation-eligible types (Annual Leave) are absent. |
| 3 | Force a `POST /api/v1/leaves` for Tom with `leaveTypeId` = Annual Leave | Server returns 403/400 -- "This leave type is not available during probation." No request created. |
| 4 | Submit a Sick Leave request for Tom | Request accepted (201 Created), assuming balance and other rules pass. |
| 5 | After Tom's status changes to Active, reload the form | Annual Leave now appears and is appliable. |

## 6. Postconditions
- No request is created for a non-probation-eligible leave type by a probation employee.
- Probation-eligible types remain available.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
