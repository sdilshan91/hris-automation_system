---
id: TC-CHR-023
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-023: Edit department name to an existing name is rejected

## 1. Test Objective
Verify that renaming a department to a name that already exists (for another department) within the same tenant is rejected, enforcing the uniqueness constraint on update operations as well.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-3
- Functional Requirements: FR-1, FR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Department "Engineering" exists.
- Department "Marketing" exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department to Edit | Marketing | Existing department |
| New Name | Engineering | Already taken by another department |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Click Edit on "Marketing" | Edit form opens with name "Marketing". |
| 2 | Change name to "Engineering" | Field accepts input. |
| 3 | Click "Save" | API call `PUT /api/v1/departments/{marketing_id}` with `{ name: "Engineering" }`. |
| 4 | Verify API returns 409 Conflict or 422 Unprocessable Entity | Error message: "A department with this name already exists." |
| 5 | Verify "Marketing" department name is unchanged in the database | Name remains "Marketing". |

## 6. Postconditions
- "Marketing" retains its original name.
- "Engineering" department is unaffected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
