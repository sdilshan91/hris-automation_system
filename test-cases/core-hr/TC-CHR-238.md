---
id: TC-CHR-238
user_story: US-CHR-009
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-238: Reactivating to Active status re-enables portal access and resumes leave accrual (FR-5)

## 1. Test Objective
Verify that when an employee's status is changed back to "active" (from suspended or inactive), the system re-enables their portal access and resumes leave accrual. This validates the positive side effects of the "active" status per FR-5.

## 2. Related Requirements
- User Story: US-CHR-009
- Functional Requirements: FR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee "Grace Kim" (`emp-009-uuid`) exists with status `suspended`.
- The employee's linked user account (`user-009-uuid`) has portal access disabled (from the suspension).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Grace Kim (emp-009-uuid) | Status: suspended, user portal access disabled |
| New Status | active | Valid transition from suspended |
| Reason | Suspension period ended | Required |
| Effective Date | 2026-06-12 | Today |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the employee cannot currently log in. | Attempting to log in as Grace Kim fails (account suspended/disabled). |
| 2 | Send `POST /api/v1/tenant/employees/emp-009-uuid/status` with body `{ "newStatus": "active", "reason": "Suspension period ended", "effectiveDate": "2026-06-12" }`. | Response 200 OK. Status changed to "active". |
| 3 | Verify portal access is re-enabled. | The employee's user account `is_active` flag is set to `true`. |
| 4 | Attempt to log in as Grace Kim. | Login succeeds. The employee can access the self-service portal. |
| 5 | Verify leave accrual is resumed. | DEFERRED -- Leave module not yet built. Verify that either: (a) a "leave accrual resumed" event/hook was dispatched, or (b) the leave accrual job no longer skips this employee. If neither exists yet, verify the employee status is `active` which the leave module will use to include them when built. |
| 6 | Verify the status badge on the profile. | Badge shows "Active" in green. |

## 6. Postconditions
- Employee status is `active`.
- Portal access is enabled.
- Leave accrual resumed (or deferred for leave module).
- Employment history entry records the reactivation.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
