---
id: TC-CHR-009
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-009: Edit department parent (reassign in hierarchy)

## 1. Test Objective
Verify that when a department's parent is changed, the hierarchy is updated correctly, the org tree visualization reflects the new parent-child relationship, and all employees in the department retain their assignment per AC-4.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-1, FR-3, FR-8
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Hierarchy exists: "Company HQ" (root) -> "Engineering" -> "Backend Team".
- A separate root department "Operations" also exists.
- "Backend Team" may have employees assigned (if employee module is available).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department to move | Backend Team | Currently under Engineering |
| Current Parent | Engineering | Current parent |
| New Parent | Operations | Target parent |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the tree view and confirm hierarchy: Company HQ -> Engineering -> Backend Team, and Operations (root) | Hierarchy is displayed correctly. |
| 2 | Click Edit on "Backend Team" | Edit form opens with Parent Department = "Engineering". |
| 3 | Change Parent Department to "Operations" | Dropdown shows "Operations" selected. |
| 4 | Click "Save" / "Update" | API call `PUT /api/v1/departments/{backend_team_id}` with `parent_department_id = operations_id`. Response is 200 OK. |
| 5 | Verify the tree view now shows: Company HQ -> Engineering (no children), Operations -> Backend Team | Hierarchy is updated; "Backend Team" moved from under "Engineering" to under "Operations". |
| 6 | Verify the flat list shows "Backend Team" with Parent = "Operations" | Parent column is updated. |
| 7 | Verify employees previously assigned to "Backend Team" retain their department assignment (if employee records exist) | Employee `department_id` still references Backend Team; no reassignment occurred. |
| 8 | Verify database: Backend Team's `parent_department_id` now references Operations' `department_id`, and both share the same `tenant_id` | FK is correctly updated; BR-3 satisfied. |

## 6. Postconditions
- Backend Team is now a child of Operations.
- Engineering has no children.
- Employee assignments within Backend Team are unchanged.
- An audit log entry records the parent change.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
