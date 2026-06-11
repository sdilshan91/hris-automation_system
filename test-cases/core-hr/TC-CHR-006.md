---
id: TC-CHR-006
user_story: US-CHR-004
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-006: Prevent circular parent-child reference (direct cycle)

## 1. Test Objective
Verify that the system prevents a department from being set as its own parent (direct self-reference cycle), enforcing FR-5 server-side circular reference detection.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-3, FR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Department "Engineering" exists as a root department in "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department | Engineering | Existing root department |
| Attempted Parent | Engineering | Self-reference (cycle) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page | "Engineering" is visible in the list. |
| 2 | Click the Edit (pencil icon) action on "Engineering" | Edit form/panel opens with current values pre-populated. |
| 3 | Open the Parent Department dropdown | Dropdown shows available departments. |
| 4 | Attempt to select "Engineering" itself as parent | Either: (a) "Engineering" is excluded from the dropdown options for itself (UI prevention), or (b) if selectable, proceed to next step. |
| 5 | If "Engineering" was selectable, click "Save" | API call `PUT /api/v1/departments/{id}` is made with `parent_department_id` set to own `department_id`. |
| 6 | Verify the API returns an error (400 Bad Request or 422 Unprocessable Entity) | Response body contains an error message indicating circular reference detected, e.g., "A department cannot be its own parent." |
| 7 | Verify the department's `parent_department_id` remains unchanged (null) in the database | No update was persisted. |

## 6. Postconditions
- "Engineering" remains a root department with `parent_department_id = null`.
- No circular reference was introduced.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
