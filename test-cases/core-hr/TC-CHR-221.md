---
id: TC-CHR-221
user_story: US-CHR-009
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-221: Status change without effective date is rejected with validation error (negative)

## 1. Test Objective
Verify that attempting to change an employee's status without providing an effective date is rejected by the API with a validation error. FR-3 mandates that every status change SHALL require an effective date.

## 2. Related Requirements
- User Story: US-CHR-009
- Functional Requirements: FR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee "Jane Doe" (`emp-002-uuid`) exists with status `active`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Jane Doe (emp-002-uuid) | Status: active |
| New Status | suspended | Valid transition |
| Reason | Valid suspension reason | Valid reason |
| Effective Date | (null / missing) | Missing required field |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/tenant/employees/emp-002-uuid/status` with body `{ "newStatus": "suspended", "reason": "Valid suspension reason" }` (effectiveDate field omitted). | Response status is 400 Bad Request. Response body contains validation error indicating effective date is required. |
| 2 | Send the same request with `"effectiveDate": null`. | Response status is 400 Bad Request. Response body contains validation error. |
| 3 | Verify employee status has not changed. | Employee status remains `active`. No employment history entries created. |
| 4 | On the UI: open the "Change Status" form, select "Suspended", enter a reason, leave Effective Date empty, and attempt to submit. | The form shows a validation error on the Effective Date field ("Effective date is required") and prevents submission. |

## 6. Postconditions
- Employee status remains `active`.
- No employment history entries were created.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
