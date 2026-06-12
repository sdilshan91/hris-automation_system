---
id: TC-CHR-017
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-017: Create department with empty required fields fails validation

## 1. Test Objective
Verify that the system rejects department creation when required fields (Department Name) are empty, providing appropriate validation messages both client-side and server-side.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department Name | (empty) | Required field left blank |
| Description | (empty) | Optional, allowed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page and click "Add Department" | Create form opens. |
| 2 | Leave the Department Name field empty | Field shows no input. |
| 3 | Click "Save" / "Create" button | Client-side validation fires before API call. |
| 4 | Verify a validation error is displayed next to the Department Name field | Error message such as "Department name is required." is shown. |
| 5 | Verify the form is NOT submitted (no API call made) | Network tab shows no outgoing request. |
| 6 | Bypass client-side validation by sending `POST /api/v1/departments` with `{ name: "" }` directly | Response status is 400 Bad Request or 422 Unprocessable Entity. |
| 7 | Verify response body contains validation error for the name field | Error indicates name is required. |
| 8 | Send `POST /api/v1/departments` with `{ name: null }` | Response status is 400 Bad Request or 422 Unprocessable Entity. |

## 6. Postconditions
- No department was created.
- Validation errors are displayed clearly for the user.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
