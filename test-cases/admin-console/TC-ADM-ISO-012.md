---
id: TC-ADM-ISO-012
user_story: US-ADM-005
module: Admin Console
priority: critical
type: security
status: draft
created: 2026-06-17
---

# TC-ADM-ISO-012: Mutating endpoints require valid tenant context + TenantAdmin authz; writes are tenant-stamped

## 1. Test Objective
Verify AC-6 / BR-1 authentication, authorization, and tenant-context requirements for the user-management API: requests with no/invalid/unresolvable tenant context are rejected; non-admin or unauthenticated callers are denied; and any rows written (membership, invitation, role, audit) are auto-stamped with the resolved `TenantId` via `TenantInterceptor` — never an attacker-supplied one.

## 2. Related Requirements
- User Story: US-ADM-005
- Acceptance Criteria: AC-6
- Functional Requirements: FR-1, FR-2, FR-4
- Business Rules: BR-1
- NOTE (platform accuracy): write stamping via `TenantInterceptor`; read isolation via EF query filters; Postgres RLS deferred (TC-ADM-005-21).

## 3. Preconditions
- Acme + Beta exist. A `TenantAdmin` (Dana) and a non-admin Employee (Eve) exist in Acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| missing subdomain | no X-Tenant-Subdomain / unknown host | unresolvable tenant |
| non-admin | Eve (Employee) | insufficient role |
| unauthenticated | no bearer | 401 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/admin/users` with NO resolvable tenant context (no subdomain/header) | Rejected — request cannot resolve a tenant; no data returned. |
| 2 | Call with an unknown/invalid tenant subdomain | Rejected — tenant not resolved; 404/400; no Acme or Beta data leaked. |
| 3 | As unauthenticated (no JWT), call any user-management endpoint | 401 Unauthorized. |
| 4 | As Eve (Employee, not TenantAdmin), call invite/deactivate/role-edit | 403 Forbidden — insufficient role (authz). |
| 5 | As Dana, invite a user and inspect the written rows | `user_tenant`, `user_invitation`, `user_tenant_role`, and `audit_log` all carry `tenant_id = Acme` (TenantInterceptor stamp) — regardless of any body-supplied value. |
| 6 | Attempt a write while supplying a Beta `tenant_id` in the body | The stamp uses the resolved Acme context; no Beta-stamped row is ever created. |

## 6. Postconditions
- No data mutated except the Acme invite in step 5/6, which is correctly Acme-stamped. Missing-context and under-privileged calls fail closed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
