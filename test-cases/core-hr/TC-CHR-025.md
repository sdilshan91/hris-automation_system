---
id: TC-CHR-025
user_story: US-CHR-004
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-CHR-025: Tenant A cannot modify or deactivate Tenant B's departments

## 1. Test Objective
Verify that a user authenticated in Tenant A cannot update, deactivate, or delete a department belonging to Tenant B, even when providing the exact department_id of Tenant B's department in the API request.

## 2. Related Requirements
- User Story: US-CHR-004
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1, BR-3

## 3. Preconditions
- Tenant "acme" exists with Tenant Admin user.
- Tenant "globex" exists with department "Globex R&D" (department_id = UUID_GRD).
- The acme user does NOT have any membership in globex.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth Context | acme | Authenticated as acme Tenant Admin |
| Target Department ID | UUID_GRD | Belongs to globex tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Tenant Admin in "acme" | JWT contains acme tenant context. |
| 2 | Send `PUT /api/v1/departments/{UUID_GRD}` with `{ name: "Hacked Name" }` using acme's JWT | Response is 404 Not Found (EF global query filter hides globex departments from acme context). |
| 3 | Send `PATCH /api/v1/departments/{UUID_GRD}/deactivate` using acme's JWT | Response is 404 Not Found. |
| 4 | Send `DELETE /api/v1/departments/{UUID_GRD}` using acme's JWT | Response is 404 Not Found. |
| 5 | Verify globex's "Globex R&D" department is unchanged | Name, status, and all fields are unmodified. `is_active = true`. |

## 6. Postconditions
- No cross-tenant modification occurred.
- Tenant B's data integrity is preserved.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
