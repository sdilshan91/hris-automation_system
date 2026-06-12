---
id: TC-CHR-187
user_story: US-CHR-007
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-187: Role-based access -- only Tenant Admin and HR Officer can create, edit, deactivate locations

## 1. Test Objective
Verify that only users with Tenant Admin or HR Officer role can perform location CRUD operations. Users with other roles (Employee, Manager) should be denied write access (403 Forbidden) but may have read access depending on permissions. This validates the preconditions in Section 2 of the user story.

## 2. Related Requirements
- User Story: US-CHR-007 (Preconditions, Section 2)
- Functional Requirements: FR-1, FR-8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Location "Test Location" exists in tenant "acme".
- Users with the following roles are set up in tenant "acme": Tenant Admin, HR Officer, Manager, Employee.

## 4. Test Data
| User | Role | Expected Create | Expected Edit | Expected Deactivate | Expected Read |
|------|------|-----------------|---------------|---------------------|---------------|
| admin@acme.com | Tenant Admin | 201 Created | 200 OK | 200 OK | 200 OK |
| hr@acme.com | HR Officer | 201 Created | 200 OK | 200 OK | 200 OK |
| manager@acme.com | Manager | 403 Forbidden | 403 Forbidden | 403 Forbidden | 200 OK |
| employee@acme.com | Employee | 403 Forbidden | 403 Forbidden | 403 Forbidden | 200 OK |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Tenant Admin; send `POST /api/v1/tenant/locations` with valid body | Response 201 Created. Location created. |
| 2 | Authenticate as HR Officer; send `POST /api/v1/tenant/locations` with valid body | Response 201 Created. Location created. |
| 3 | Authenticate as Manager; send `POST /api/v1/tenant/locations` with valid body | Response 403 Forbidden. No location created. |
| 4 | Authenticate as Employee; send `POST /api/v1/tenant/locations` with valid body | Response 403 Forbidden. No location created. |
| 5 | Authenticate as Manager; send `PUT /api/v1/tenant/locations/{id}` | Response 403 Forbidden. Location not modified. |
| 6 | Authenticate as Employee; send `PUT /api/v1/tenant/locations/{id}` | Response 403 Forbidden. Location not modified. |
| 7 | Authenticate as Manager; send `POST /api/v1/tenant/locations/{id}/deactivate` | Response 403 Forbidden. Location not deactivated. |
| 8 | Authenticate as Employee; send `POST /api/v1/tenant/locations/{id}/deactivate` | Response 403 Forbidden. Location not deactivated. |
| 9 | Authenticate as Manager; send `GET /api/v1/tenant/locations` | Response 200 OK. Locations listed (read access allowed). |
| 10 | Authenticate as Employee; send `GET /api/v1/tenant/locations` | Response 200 OK. Locations listed (read access allowed). |

## 6. Postconditions
- Only locations created by authorized roles (Tenant Admin, HR Officer) exist.
- No unauthorized modifications were persisted.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
