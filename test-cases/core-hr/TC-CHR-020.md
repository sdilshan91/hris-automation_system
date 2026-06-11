---
id: TC-CHR-020
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: blocked
created: 2026-06-11
---

# TC-CHR-020: Assign department manager (BLOCKED -- depends on US-CHR-001)

## 1. Test Objective
Verify that a department can have a manager assigned via the `manager_employee_id` FK to the employee table, and that the manager field displays the employee's name and avatar in the department list and form.

**STATUS: BLOCKED** -- This test case depends on US-CHR-001 (Employee Management) which has not yet been built. The `manager_employee_id` FK references the `employee` table from US-CHR-001. This test case should be unblocked and executed once US-CHR-001 is implemented.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-1 (Department Manager optional employee picker)
- Functional Requirements: FR-4
- Business Rules: BR-2
- Dependencies: US-CHR-001 (Employees)

## 3. Preconditions
- **[BLOCKED]** US-CHR-001 (Employee Management) must be implemented first.
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Department "Engineering" exists.
- At least one employee record exists in "acme" tenant (requires US-CHR-001).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department | Engineering | Existing department |
| Manager Employee | Jane Smith (employee_id: UUID) | Active employee in same tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page | Department list loads. |
| 2 | Click Edit on "Engineering" | Edit form opens. |
| 3 | Open the Department Manager employee picker field | Searchable employee autocomplete appears with avatar + name display (per UI/UX notes). |
| 4 | Search for and select "Jane Smith" | Employee is selected; avatar and name are displayed in the field. |
| 5 | Click "Save" | API call `PUT /api/v1/departments/{id}` with `manager_employee_id` set to Jane's employee_id. Response is 200 OK. |
| 6 | Verify the department list shows "Jane Smith" in the Manager column for "Engineering" | Manager name (and optionally avatar) is displayed. |
| 7 | Verify database: `manager_employee_id` references Jane's employee record in the same tenant | FK is valid; employee belongs to the same tenant. |
| 8 | Assign a different manager and verify the old one is replaced | BR-2: A department can have at most one manager. |
| 9 | Clear the manager field and save | `manager_employee_id` is set to null. Manager column shows "-" or empty. |

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
