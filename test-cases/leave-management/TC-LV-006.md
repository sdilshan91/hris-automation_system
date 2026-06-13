---
id: TC-LV-006
user_story: US-LV-001
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-006: Negative entitlement rejected; zero allowed for unpaid leave

## 1. Test Objective
Verify that creating or editing a leave type with a negative annual entitlement value is rejected with a validation error, but zero entitlement is allowed specifically for unpaid leave types (BR-3).

## 2. Related Requirements
- User Story: US-LV-001
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists.
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Attempt 1 - Name | Negative Test Leave | With entitlement = -5 |
| Attempt 1 - Entitlement | -5.00 | Negative, should be rejected |
| Attempt 2 - Name | Unpaid Leave | With entitlement = 0 |
| Attempt 2 - Entitlement | 0.00 | Zero, should be accepted |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click "Add Leave Type" and enter Name = "Negative Test Leave", Code = "NTL" | Form fields accept input. |
| 2 | Enter Annual Entitlement = -5 | Client-side validation highlights the field with error: "Entitlement must be zero or a positive number." |
| 3 | Attempt to submit the form | Form submission is blocked by client-side validation. |
| 4 | Bypass client validation and send `POST /api/v1/leave-types` with `{ annual_entitlement: -5 }` | API returns 400 Bad Request with validation error: "Entitlement values must be positive numbers; zero entitlement is allowed for unpaid leave types." |
| 5 | Create a new leave type: Name = "Unpaid Leave", Code = "UPL", Annual Entitlement = 0.00, fill other required fields | Form accepts zero entitlement without validation error. |
| 6 | Click Save | API returns 201 Created. Leave type "Unpaid Leave" created with annual_entitlement = 0.00. |
| 7 | Verify the "Unpaid Leave" record in database | `annual_entitlement = 0.00` is stored correctly. |

## 6. Postconditions
- No leave type with negative entitlement exists.
- "Unpaid Leave" with zero entitlement is created successfully.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
