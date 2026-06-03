---
id: TC-AUTH-041
user_story: US-AUTH-006
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-03
---

# TC-AUTH-041: System role creation in a regular tenant is rejected

## 1. Test Objective
Verify that system-level roles (System Super Admin, System Support, System Billing, System Compliance) cannot be created within a regular tenant. These roles must exist only in the system tenant to maintain the platform's security boundary between system administration and tenant operations.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-2 (inverse/negative)
- Functional Requirements: FR-9, FR-2
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" is a regular tenant (not the system tenant) in `active` state.
- User `admin@acme.com` is authenticated with `Tenant Admin` role (has `Role.Manage` permission).
- The permission catalog includes system-level permission names.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin user | admin@acme.com | Tenant Admin in regular tenant |
| System role name 1 | System Super Admin | Reserved system role name |
| System role name 2 | System Support | Reserved system role name |
| System role name 3 | System Billing | Reserved system role name |
| System role name 4 | System Compliance | Reserved system role name |
| Tenant | acme (acme.yourhrm.com) | Regular tenant, not system tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `admin@acme.com` at `acme.yourhrm.com` and obtain JWT. | JWT issued with Tenant Admin role, `tenant_id` = acme tenant UUID. |
| 2 | Send `POST /api/v1/tenant/roles` with body `{ "name": "System Super Admin", "description": "Attempting system role", "permissions": ["Tenant.Settings.Manage"] }`. | HTTP 400 Bad Request (or 403 Forbidden). Response indicates system roles cannot be created in regular tenants. |
| 3 | Send `POST /api/v1/tenant/roles` with body `{ "name": "System Support", "description": "Attempting system role", "permissions": ["User.Manage"] }`. | HTTP 400 Bad Request (or 403 Forbidden). Same rejection message. |
| 4 | Send `POST /api/v1/tenant/roles` with body `{ "name": "System Billing", "description": "Attempting system role", "permissions": ["Payroll.View"] }`. | HTTP 400 Bad Request (or 403 Forbidden). Same rejection message. |
| 5 | Send `POST /api/v1/tenant/roles` with body `{ "name": "System Compliance", "description": "Attempting system role", "permissions": ["Audit.View"] }`. | HTTP 400 Bad Request (or 403 Forbidden). Same rejection message. |
| 6 | Send `GET /api/v1/tenant/roles` and verify no system roles appear in the list. | HTTP 200 OK. Role list contains only built-in tenant roles and any custom roles. No "System Super Admin", "System Support", "System Billing", or "System Compliance" roles appear. |
| 7 | Verify the rejection attempts are logged in the security/audit log. | Log entries exist for each rejected system role creation attempt. |

## 6. Postconditions
- No system roles exist in the regular tenant.
- The role table for tenant "acme" is unchanged.
- Security log entries record the rejected attempts.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
