---
id: TC-AUTH-032
user_story: US-AUTH-005
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-03
---

# TC-AUTH-032: Tenant admin updates MFA policy

## 1. Test Objective
Verify that a tenant admin can update the tenant's MFA policy and required roles via `PUT /api/v1/tenant/auth-settings`, that the changes are persisted and audited, and that non-admin users are denied access to this endpoint.

## 2. Related Requirements
- User Story: US-AUTH-005
- Acceptance Criteria: AC-1, AC-6
- Functional Requirements: FR-6, FR-8
- Business Rules: BR-1, BR-5

## 3. Preconditions
- Tenant "acme" exists and is in `active` state.
- Current tenant auth settings: `mfaPolicy = "off"`, `mfaRequiredRoles = []`.
- User `admin@acme.com` has the "Tenant Admin" role.
- User `employee@acme.com` has the "Employee" role (no admin privileges).
- Both users are authenticated with valid JWT tokens.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin User | admin@acme.com | Tenant Admin role |
| Non-Admin User | employee@acme.com | Employee role |
| Endpoint | PUT /api/v1/tenant/auth-settings | Policy update |
| Read Endpoint | GET /api/v1/tenant/auth-settings | Policy read |
| New Policy | `{ mfaPolicy: "required", mfaRequiredRoles: ["Tenant Admin", "HR Officer"] }` | Target state |
| Audit Event | tenant_mfa_policy_updated | Expected audit type |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/auth-settings` as `admin@acme.com` | HTTP 200 with current settings: `{ mfaPolicy: "off", mfaRequiredRoles: [] }`. |
| 2 | Send `PUT /api/v1/tenant/auth-settings` as `admin@acme.com` with `{ mfaPolicy: "required", mfaRequiredRoles: ["Tenant Admin", "HR Officer"] }` | HTTP 200 with updated settings echoed in the response. |
| 3 | Verify the tenant settings in the database reflect the update | `mfa_policy = "required"`, `mfa_required_roles = ["Tenant Admin", "HR Officer"]`. |
| 4 | Verify a `tenant_mfa_policy_updated` audit event is logged | Audit record contains `tenant_id`, `user_id` (admin), old policy, new policy, timestamp. |
| 5 | Send `GET /api/v1/tenant/auth-settings` as `admin@acme.com` | HTTP 200 returns the updated policy, confirming persistence. |
| 6 | Send `PUT /api/v1/tenant/auth-settings` as `employee@acme.com` with `{ mfaPolicy: "off" }` | HTTP 403 Forbidden. Non-admin users cannot modify tenant auth settings. |
| 7 | Verify settings remain unchanged after the rejected request | `mfa_policy` is still "required". |
| 8 | Send `PUT /api/v1/tenant/auth-settings` as `admin@acme.com` with `{ mfaPolicy: "invalid_value" }` | HTTP 400 Bad Request with validation error. Invalid enum values are rejected. |
| 9 | Send `PUT /api/v1/tenant/auth-settings` as `admin@acme.com` with `{ mfaPolicy: "optional", mfaRequiredRoles: [] }` | HTTP 200. Policy reverts to optional with no required roles. |
| 10 | Verify a second `tenant_mfa_policy_updated` audit event is logged for the revert | Audit trail tracks all policy changes. |

## 6. Postconditions
- Tenant auth settings reflect the final state from the last successful update.
- Audit log contains entries for each policy change.
- Non-admin access attempts are rejected without side effects.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
