---
id: TC-CHR-020
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-11
updated: 2026-06-12
unblocked_by: US-CHR-001
---

# TC-CHR-020: Assign department manager

## 1. Test Objective
Verify that a department can have a manager assigned via the `manager_employee_id` FK to the employee table, and that the manager field displays the employee's name and avatar in the department list and form. Previously BLOCKED on US-CHR-001 -- now unblocked.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-1 (Department Manager optional employee picker)
- Functional Requirements: FR-4
- Business Rules: BR-2
- Dependencies: US-CHR-001 (Employees) -- now available

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Department "Engineering" exists.
- At least two employee records exist in "acme" tenant: "Jane Smith" and "Bob Wilson" (created via US-CHR-001 employee creation flow).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department | Engineering | Existing department |
| Manager Employee 1 | Jane Smith (employee_id: UUID) | Active employee in same tenant |
| Manager Employee 2 | Bob Wilson (employee_id: UUID) | Active employee in same tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page | Department list loads. |
| 2 | Click Edit on "Engineering" | Edit form opens. |
| 3 | Open the Department Manager employee picker field | Searchable employee autocomplete appears with avatar + name display (per UI/UX notes). Only active employees from the current tenant are listed. |
| 4 | Search for and select "Jane Smith" | Employee is selected; avatar and name are displayed in the field. |
| 5 | Click "Save" | API call `PUT /api/v1/tenant/departments/{id}` with `manager_employee_id` set to Jane's employee_id. Response is 200 OK. |
| 6 | Verify the department list shows "Jane Smith" in the Manager column for "Engineering" | Manager name (and optionally avatar) is displayed. |
| 7 | Verify database: `manager_employee_id` references Jane's employee record in the same tenant | FK is valid; employee belongs to the same tenant. |
| 8 | Edit "Engineering" again and change the manager to "Bob Wilson" | Manager is updated. Verify the old manager (Jane) is replaced. BR-2: At most one manager per department. |
| 9 | Verify the department list now shows "Bob Wilson" as manager | Manager column updated. |
| 10 | Clear the manager field and save | `manager_employee_id` is set to null. Manager column shows "-" or empty. |
| 11 | Verify database: `manager_employee_id` is null | Nullable FK confirmed. |

## 6. Postconditions
- Department manager assignment works correctly.
- At most one manager per department (BR-2).
- Manager field is nullable and can be cleared.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
