---
id: TC-CHR-011
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-011: Deactivate department with no active employees (success)

## 1. Test Objective
Verify that a department with zero active employees can be successfully deactivated, resulting in soft-delete behavior: the department is hidden from dropdowns but remains visible in admin views.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-5
- Functional Requirements: FR-6, FR-7
- Non-Functional Requirements: NFR-5
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Department "Legacy Systems" exists with `is_active = true` and zero active employees assigned.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department | Legacy Systems | No active employees |
| Employee Count | 0 | No employees assigned |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page | "Legacy Systems" is visible with Employee Count = 0, Status = Active. |
| 2 | Click the Deactivate (archive icon) action on "Legacy Systems" | A confirmation dialog appears: "Are you sure you want to deactivate Legacy Systems?" |
| 3 | Confirm the deactivation | API call to deactivate endpoint is made. Response status is 200 OK. |
| 4 | Verify "Legacy Systems" now shows Status = "Inactive" in the admin department list | Department is still visible in the admin view but marked as inactive. |
| 5 | Verify that when creating a new department, the Parent Department dropdown does NOT include "Legacy Systems" | Inactive departments are excluded from selection dropdowns (FR-7). |
| 6 | Verify that when assigning a department to an employee (if available), "Legacy Systems" is NOT in the dropdown | Deactivated departments cannot be assigned to new employees (BR-5). |
| 7 | Verify database: `is_active = false`, `is_deleted = false` | Soft-delete: record exists but is deactivated, not hard-deleted. |
| 8 | Verify an audit log entry exists for the deactivation | Audit record contains `action: department_deactivated`, department_id, tenant_id, and timestamp. |

## 6. Postconditions
- "Legacy Systems" has `is_active = false`, `is_deleted = false`.
- Department is visible in admin views but hidden from assignment/selection dropdowns.
- An audit log entry of type `department_deactivated` has been recorded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
