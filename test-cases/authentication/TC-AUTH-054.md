---
id: TC-AUTH-054
user_story: US-AUTH-007
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-09
---

# TC-AUTH-054: Resolved tenant context prevents cross-tenant data exposure

## 1. Test Objective
Verify that a request resolved from one tenant subdomain cannot read, write, or cache data from another tenant, even when a token or request payload attempts to reference another tenant.

## 2. Related Requirements
- User Story: US-AUTH-007
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-6, FR-10
- Business Rules: BR-4
- Data Requirements: Redis cache key `t:subdomain:{slug}`, `ITenantContext.TenantId`

## 3. Preconditions
- Tenants `acme` and `globex` exist with active status.
- User `employee@acme.com` belongs only to `acme`.
- User `employee@globex.com` belongs only to `globex`.
- Both tenants have distinguishable tenant-scoped records.
- Redis is available and may already contain entries for both subdomains.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A host | acme.yourhrm.com | Resolved request tenant |
| Tenant B host | globex.yourhrm.com | Tenant that must not leak |
| Tenant A cache key | t:subdomain:acme | Must contain only acme DTO |
| Tenant B cache key | t:subdomain:globex | Must contain only globex DTO |
| Mismatched token | globex JWT used on acme host | Must be rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate `employee@acme.com` on `https://acme.yourhrm.com`. | Token is issued for acme tenant only. |
| 2 | Call a tenant-scoped read endpoint on `acme.yourhrm.com`. | Response includes only acme data. No globex IDs, names, roles, or settings appear. |
| 3 | Use a valid `globex` JWT against `https://acme.yourhrm.com/api/v1/tenant/users` or equivalent tenant endpoint. | Request is rejected with HTTP 401 or 403 because token tenant and resolved host tenant do not match. |
| 4 | Submit a write request to `acme.yourhrm.com` with a payload containing `tenantId` for globex. | Request is rejected or server ignores payload tenant ID and uses resolved acme context only. No globex data is changed. |
| 5 | Inspect Redis entries for `t:subdomain:acme` and `t:subdomain:globex`. | Cache keys and serialized tenant DTOs are separated by subdomain; no shared or overwritten tenant data exists. |
| 6 | Verify Serilog properties for the acme request. | Logs include acme `tenant_id` and do not switch to globex because of token or payload input. |

## 6. Postconditions
- Tenant data remains isolated between `acme` and `globex`.
- No cross-tenant cache pollution occurs.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
