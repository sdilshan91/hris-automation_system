---
id: TC-CHR-308
user_story: US-CHR-012
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-CHR-308: Only Tenant Admin can manage custom fields -- role-based access

## 1. Test Objective
Verify that only users with the Tenant Admin role can create, update, reorder, and deactivate custom field definitions. HR Officers, Managers, and Employees attempting to access custom field management endpoints are denied with 403 Forbidden. This validates the preconditions of US-CHR-012 (Section 2).

## 2. Related Requirements
- User Story: US-CHR-012
- Preconditions: Section 2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Users exist with roles: Tenant Admin, HR Officer, Manager, Employee.
- Custom field "T-Shirt Size" exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant Admin | admin@acme.test | Should have full access |
| HR Officer | hr@acme.test | Should be denied management access |
| Manager | mgr@acme.test | Should be denied management access |
| Employee | emp@acme.test | Should be denied management access |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Tenant Admin. `POST /api/v1/tenant/custom-fields` with valid body. | HTTP 201 Created. Field is created. |
| 2 | Authenticate as HR Officer. `POST /api/v1/tenant/custom-fields` with valid body. | HTTP 403 Forbidden. |
| 3 | Authenticate as Manager. `POST /api/v1/tenant/custom-fields` with valid body. | HTTP 403 Forbidden. |
| 4 | Authenticate as Employee. `POST /api/v1/tenant/custom-fields` with valid body. | HTTP 403 Forbidden. |
| 5 | Authenticate as HR Officer. `PUT /api/v1/tenant/custom-fields/{id}` to update the field. | HTTP 403 Forbidden. |
| 6 | Authenticate as HR Officer. `POST /api/v1/tenant/custom-fields/{id}/deactivate`. | HTTP 403 Forbidden. |
| 7 | Authenticate as Tenant Admin. `GET /api/v1/tenant/custom-fields?entityType=Employee`. | HTTP 200 OK with field list (read access for configuration). |

## 6. Postconditions
- Only Tenant Admin can manage custom field definitions.
- Other roles are denied with 403 on management endpoints.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
