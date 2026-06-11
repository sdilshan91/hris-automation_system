---
id: TC-CHR-012
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-012: Deactivate parent department blocked when it has active child departments

## 1. Test Objective
Verify that the system prevents deactivation of a parent department that still has active child departments, enforcing BR-6: "Deleting a parent department requires reassigning or deactivating all child departments first."

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-5 (extended)
- Functional Requirements: FR-6
- Business Rules: BR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Hierarchy: "Engineering" (root, active) -> "Backend Team" (child, active).
- "Engineering" has zero active employees assigned directly.
- "Backend Team" is still active.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Parent Department | Engineering | Root, zero direct employees |
| Child Department | Backend Team | Active child |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page | "Engineering" and "Backend Team" are visible. |
| 2 | Click the Deactivate action on "Engineering" | Confirmation dialog appears. |
| 3 | Confirm the deactivation | API call to deactivate "Engineering" is made. |
| 4 | Verify API returns an error (409 Conflict or 422 Unprocessable Entity) | Response body contains a message such as: "This department has active child departments. Please reassign or deactivate them before deactivating this department." |
| 5 | Verify the error is displayed in the UI | User sees the child department reassignment instruction. |
| 6 | Verify "Engineering" remains active | Status = Active; `is_active = true` in the database. |
| 7 | Deactivate "Backend Team" first (which has zero employees) | "Backend Team" is deactivated successfully. |
| 8 | Now attempt to deactivate "Engineering" again | This time, with no active children and no employees, deactivation succeeds (200 OK). "Engineering" is now inactive. |

## 6. Postconditions
- After step 6: "Engineering" remains active due to active children.
- After step 8: Both "Engineering" and "Backend Team" are deactivated.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
