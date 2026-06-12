---
id: TC-CHR-002
user_story: US-CHR-004
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-002: Create a child department with parent assignment

## 1. Test Objective
Verify that a Tenant Admin can create a department with a parent department selected, establishing a valid parent-child hierarchy relationship, and that the tree view reflects the new relationship.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-1, AC-2, AC-4
- Functional Requirements: FR-1, FR-3, FR-8
- Business Rules: BR-3, BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- A root department "Engineering" already exists in the "acme" tenant.
- No department named "Backend Team" exists in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Department Name | Backend Team | Required, unique within tenant |
| Parent Department | Engineering | Existing root department |
| Description | Server-side development team | Optional |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page | Department list page loads; "Engineering" is visible. |
| 2 | Click "Add Department" button | Create form/panel appears with all required fields. |
| 3 | Enter "Backend Team" in the Department Name field | Field accepts the input. |
| 4 | Open the Parent Department dropdown | Searchable dropdown displays existing departments with hierarchy indentation (dashes or tree lines). "Engineering" is shown at root level. |
| 5 | Select "Engineering" as the parent department | Dropdown closes; "Engineering" is displayed as the selected parent. |
| 6 | Enter "Server-side development team" in the Description field | Field accepts input. |
| 7 | Click "Save" / "Create" button | Loading indicator appears. |
| 8 | Observe API call `POST /api/v1/departments` with body containing `parent_department_id` matching Engineering's UUID | Response status is 201 Created. Response body shows `parent_department_id` set to Engineering's department_id. |
| 9 | Verify "Backend Team" appears in the department list with Parent column showing "Engineering" | Row displays correctly with parent reference. |
| 10 | Toggle to tree view | "Backend Team" appears as a child node under "Engineering". Expanding "Engineering" reveals "Backend Team" nested beneath it. |
| 11 | Verify the `parent_department_id` FK in the database references Engineering's `department_id` and both share the same `tenant_id` | FK constraint is satisfied; tenant_id matches (BR-3). |

## 6. Postconditions
- A new department "Backend Team" exists with `parent_department_id` referencing "Engineering".
- Both departments share the same `tenant_id`.
- The tree view shows the correct parent-child relationship.
- An audit log entry of type `department_created` has been recorded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
