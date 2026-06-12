---
id: TC-CHR-021
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-021: Parent department must belong to the same tenant

## 1. Test Objective
Verify that the system prevents setting a parent department from a different tenant, enforcing BR-3: "A department's parent must belong to the same tenant." This tests server-side validation since the UI dropdown should only show same-tenant departments.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-3
- Business Rules: BR-3
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with department "Acme Engineering" (department_id = UUID_A).
- Tenant "globex" exists with department "Globex Engineering" (department_id = UUID_G).
- A user with Tenant Admin role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Acme Department | Acme Engineering | Tenant A department |
| Globex Department ID | UUID_G | Tenant B department (cross-tenant) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Departments page in "acme" tenant | Only acme's departments are visible (tenant-scoped query). |
| 2 | Click "Add Department" and open the Parent Department dropdown | Dropdown only shows departments belonging to "acme" tenant. "Globex Engineering" does NOT appear. |
| 3 | Bypass the UI: send `POST /api/v1/departments` with `{ name: "New Dept", parent_department_id: UUID_G }` where UUID_G belongs to "globex" | Response status is 400 Bad Request or 404 Not Found. |
| 4 | Verify error message indicates the parent department was not found or is invalid | Message such as "Parent department not found" (since EF query filters scope to current tenant, the globex department is invisible). |
| 5 | Verify no department was created | Database state is unchanged. |

## 6. Postconditions
- No cross-tenant parent assignment occurred.
- Tenant isolation on parent department lookups is enforced.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
