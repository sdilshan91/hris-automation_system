---
id: TC-LV-013
user_story: US-LV-001
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-013: Only Leave.Configure / Tenant Admin can manage leave types (role check)

## 1. Test Objective
Verify that only users with `Leave.Configure` permission or `Tenant.Admin` role can create, edit, or deactivate leave types. Users with Employee or Manager roles are rejected with 403 Forbidden.

## 2. Related Requirements
- User Story: US-LV-001
- Preconditions: Section 2
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with leave type "Annual Leave".
- Users with different roles exist: Tenant Admin, HR Officer (with Leave.Configure), Manager (without Leave.Configure), Employee.

## 4. Test Data
| Role | Permission | Expected Result |
|------|-----------|-----------------|
| Tenant Admin | Full | 201/200 (allowed) |
| HR Officer (Leave.Configure) | Leave.Configure | 201/200 (allowed) |
| Manager | No Leave.Configure | 403 Forbidden |
| Employee | No Leave.Configure | 403 Forbidden |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Tenant Admin, `POST /api/v1/leave-types` with valid data | 201 Created. Leave type created successfully. |
| 2 | As HR Officer with Leave.Configure, `PUT /api/v1/leave-types/{id}` with updated data | 200 OK. Leave type updated successfully. |
| 3 | As Manager (no Leave.Configure), `POST /api/v1/leave-types` with valid data | 403 Forbidden. Error: "You do not have permission to manage leave types." |
| 4 | As Employee, `POST /api/v1/leave-types` with valid data | 403 Forbidden. Same error message. |
| 5 | As Manager, `PUT /api/v1/leave-types/{id}` | 403 Forbidden. |
| 6 | As Employee, `PATCH /api/v1/leave-types/{id}` (deactivation) | 403 Forbidden. |
| 7 | As Manager, `DELETE /api/v1/leave-types/{id}` | 403 Forbidden (or 405 if DELETE not implemented). |

## 6. Postconditions
- Leave types can only be managed by authorized roles.
- Unauthorized role attempts are logged in security audit.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
