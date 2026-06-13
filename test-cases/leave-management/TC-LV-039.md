---
id: TC-LV-039
user_story: US-LV-002
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-039: Only Leave.Configure permission can manage entitlement rules

## 1. Test Objective
Verify that only users with `Leave.Configure` permission (typically HR Officer and Tenant Admin) can create, update, and delete entitlement rules and per-employee overrides. Users without this permission (Employee, Manager) are denied access with 403 Forbidden.

## 2. Related Requirements
- User Story: US-LV-002
- Preconditions: Section 2
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Users with the following roles exist:
  - HR Officer (has `Leave.Configure`)
  - Tenant Admin (has `Leave.Configure`)
  - Regular Employee (does NOT have `Leave.Configure`)
  - Manager (does NOT have `Leave.Configure`)
- An existing entitlement rule for "Annual Leave" exists.

## 4. Test Data
| Role | Has Leave.Configure | Expected Access |
|------|-------------------|-----------------|
| HR Officer | Yes | Full CRUD |
| Tenant Admin | Yes | Full CRUD |
| Employee | No | 403 on all mutations |
| Manager | No | 403 on all mutations |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As HR Officer, `POST /api/v1/leave-entitlement-rules` with valid data | 201 Created. Rule created successfully. |
| 2 | As HR Officer, `PUT /api/v1/leave-entitlement-rules/{id}` with valid update | 200 OK. Rule updated. |
| 3 | As HR Officer, `DELETE /api/v1/leave-entitlement-rules/{id}` | 200 OK. Rule deleted. |
| 4 | As HR Officer, `POST /api/v1/leave-entitlement-overrides` | 201 Created. Override created. |
| 5 | As Tenant Admin, `POST /api/v1/leave-entitlement-rules` with valid data | 201 Created. Tenant Admin has access. |
| 6 | As Employee, `POST /api/v1/leave-entitlement-rules` with valid data | 403 Forbidden. Error message does not leak internal details. |
| 7 | As Employee, `PUT /api/v1/leave-entitlement-rules/{id}` | 403 Forbidden. |
| 8 | As Employee, `DELETE /api/v1/leave-entitlement-rules/{id}` | 403 Forbidden. |
| 9 | As Employee, `POST /api/v1/leave-entitlement-overrides` | 403 Forbidden. |
| 10 | As Manager, `POST /api/v1/leave-entitlement-rules` with valid data | 403 Forbidden. |
| 11 | As Manager, `PUT /api/v1/leave-entitlement-rules/{id}` | 403 Forbidden. |
| 12 | As Manager, `DELETE /api/v1/leave-entitlement-rules/{id}` | 403 Forbidden. |
| 13 | As Employee, `GET /api/v1/leave-entitlement-rules` (read-only list) | 200 OK or 403 depending on policy (employees may view rules but not edit). |

## 6. Postconditions
- No unauthorized mutations occurred.
- 403 responses do not leak stack traces, internal entity IDs, or permission names.
- HR Officer and Tenant Admin can perform all entitlement management operations.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
