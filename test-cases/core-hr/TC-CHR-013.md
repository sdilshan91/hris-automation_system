---
id: TC-CHR-013
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-013: Soft delete -- departments are never hard deleted

## 1. Test Objective
Verify that department deletion is always a soft delete (setting `is_deleted = true`) and that hard deletion is never performed, per FR-7. Deactivated/deleted departments must remain in the database and be visible in admin views.

## 2. Related Requirements
- User Story: US-CHR-004
- Functional Requirements: FR-7
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Department "Temp Project" exists with `is_active = true`, zero active employees, and no active children.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department | Temp Project | Target for deletion |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page | "Temp Project" is visible. |
| 2 | Deactivate "Temp Project" (or delete if a delete action is available) | Action succeeds (200 OK). |
| 3 | Verify `is_active = false` in the database | Deactivation flag is set. |
| 4 | If a "Delete" action exists (beyond deactivate), invoke it | API call `DELETE /api/v1/departments/{id}` is made. Response is 200 OK (or 204 No Content). |
| 5 | Query the database directly for the department record | Record still exists with `is_deleted = true`. The row has NOT been removed from the `department` table. |
| 6 | Verify the department is no longer visible in the standard department list | Soft-deleted departments are filtered out of normal views. |
| 7 | Verify the department IS visible in an admin/audit view (if implemented) or retrievable via a query with `include_deleted` parameter | Soft-deleted data is still accessible for audit/compliance purposes. |
| 8 | Attempt a raw `DELETE FROM department WHERE department_id = ...` equivalent through the API (if any endpoint exists) | No API endpoint performs hard delete. If attempted, it should return 404 or 405 Method Not Allowed. |

## 6. Postconditions
- The department record exists in the database with `is_deleted = true`.
- No hard deletion has occurred.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
