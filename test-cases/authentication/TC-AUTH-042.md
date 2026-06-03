---
id: TC-AUTH-042
user_story: US-AUTH-006
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-06-03
---

# TC-AUTH-042: Remove Tenant Owner role from sole owner is rejected

## 1. Test Objective
Verify that the system prevents removing the Tenant Owner role from the last remaining tenant owner. At least one user must always hold the Tenant Owner role to ensure the tenant remains administrable. This covers both direct role removal and indirect removal via role reassignment.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-3 (negative path)
- Functional Requirements: FR-8
- Business Rules: BR-6

## 3. Preconditions
- Tenant "acme" is provisioned and in `active` state.
- User `owner@acme.com` is the sole user with the `Tenant Owner` role in tenant "acme".
- User `owner@acme.com` is authenticated (or `admin@acme.com` with Tenant Admin role is performing the operation).
- No other user in tenant "acme" holds the `Tenant Owner` role.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Sole owner | owner@acme.com | Only Tenant Owner in tenant |
| Admin user | admin@acme.com | Tenant Admin (not owner) |
| Tenant Owner role ID | {tenant_owner_role_id} | Built-in Tenant Owner role |
| Employee role ID | {employee_role_id} | Built-in Employee role |
| Tenant | acme (acme.yourhrm.com) | Active tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `admin@acme.com` (Tenant Admin) at `acme.yourhrm.com`. | JWT issued with Tenant Admin role. |
| 2 | Send `PATCH /api/v1/tenant/users/{owner_user_id}` with body `{ "roleIds": ["{employee_role_id}"] }` (removing Tenant Owner, leaving only Employee). | HTTP 400 Bad Request. Response body contains error: "Cannot remove the Tenant Owner role from the sole owner. At least one user must hold the Tenant Owner role." |
| 3 | Send `PATCH /api/v1/tenant/users/{owner_user_id}` with body `{ "roleIds": [] }` (removing all roles). | HTTP 400 Bad Request. Same error message about sole owner protection. |
| 4 | Verify `owner@acme.com` still has the Tenant Owner role by sending `GET /api/v1/tenant/users/{owner_user_id}`. | HTTP 200 OK. User record shows `roles` includes "Tenant Owner". |
| 5 | Add a second Tenant Owner: send `PATCH /api/v1/tenant/users/{admin_user_id}` with body `{ "roleIds": ["{tenant_admin_role_id}", "{tenant_owner_role_id}"] }`. | HTTP 200 OK. `admin@acme.com` now also holds Tenant Owner role. |
| 6 | Now send `PATCH /api/v1/tenant/users/{owner_user_id}` with body `{ "roleIds": ["{employee_role_id}"] }` again. | HTTP 200 OK. Since `admin@acme.com` still holds Tenant Owner, removing it from `owner@acme.com` is now allowed. |
| 7 | Verify final state: `GET /api/v1/tenant/users` and check Tenant Owner assignments. | Exactly one user (`admin@acme.com`) holds the Tenant Owner role. `owner@acme.com` now has only Employee. |

## 6. Postconditions
- The system always maintains at least one Tenant Owner.
- After step 6, the ownership has been transferred successfully.
- Audit log records both the rejected and the successful role changes.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
