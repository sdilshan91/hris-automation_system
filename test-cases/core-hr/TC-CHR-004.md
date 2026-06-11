---
id: TC-CHR-004
user_story: US-CHR-004
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-CHR-004: Same department name allowed in different tenants

## 1. Test Objective
Verify that two different tenants can each have a department with the same name, confirming that the uniqueness constraint is tenant-scoped (not global) per BR-1 and AC-3.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-3
- Functional Requirements: FR-2
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active` and has a department named "Engineering".
- Tenant "globex" exists with status `active` and does NOT have a department named "Engineering".
- A user with Tenant Admin role is authenticated in the "globex" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A Subdomain | acme.yourhrm.com | Has "Engineering" department |
| Tenant B Subdomain | globex.yourhrm.com | No "Engineering" department yet |
| Department Name | Engineering | Same name as Tenant A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Tenant Admin in the "globex" tenant context | User is logged in with globex tenant context resolved. |
| 2 | Navigate to the Departments management page | Page loads showing globex's departments (no "Engineering"). |
| 3 | Click "Add Department" button | Create form appears. |
| 4 | Enter "Engineering" in the Department Name field | Field accepts the input. |
| 5 | Click "Save" / "Create" button | Request is submitted. |
| 6 | Observe API call `POST /api/v1/departments` with globex tenant context | Response status is 201 Created. Department is created with `tenant_id` matching globex. |
| 7 | Verify "Engineering" now appears in globex's department list | Department is visible and associated with globex. |
| 8 | Switch to "acme" tenant context and verify "Engineering" still exists independently | Acme's "Engineering" department is unaffected; it has a different `department_id` and `tenant_id`. |
| 9 | Verify database contains two "Engineering" departments with different `tenant_id` values | Both records exist; uniqueness is per-tenant, not global. |

## 6. Postconditions
- Both tenants have a department named "Engineering" with distinct `department_id` and `tenant_id` values.
- Neither tenant's data was affected by the other's operations.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
