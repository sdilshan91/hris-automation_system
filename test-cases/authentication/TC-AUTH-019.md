---
id: TC-AUTH-019
user_story: US-AUTH-007
module: Authentication
priority: critical
type: functional
status: draft
created: 2026-05-11
---

# TC-AUTH-019: Valid subdomain resolves to correct tenant

## 1. Test Objective
Verify that the Tenant Resolution Middleware correctly extracts the subdomain from the Host header, resolves it to the correct tenant, populates the ITenantContext, and allows the request to proceed with tenant-scoped data.

## 2. Related Requirements
- User Story: US-AUTH-007
- Acceptance Criteria: AC-1, AC-6
- Functional Requirements: FR-1, FR-2, FR-5, FR-6, FR-9, FR-10

## 3. Preconditions
- Tenant "acme" exists in the `tenant` table with status `active`, subdomain `acme`, and a valid plan.
- Redis is available for caching.
- Wildcard DNS is configured for `*.yourhrm.com`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Valid provisioned tenant |
| Tenant ID | (acme UUID) | Expected in ITenantContext |
| Tenant Status | active | Accessible state |
| Tenant Plan | (plan details) | From tenant configuration |
| Redis cache key | t:subdomain:acme | Cache lookup key |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send a request to `https://acme.yourhrm.com/api/v1/auth/login` | The Tenant Resolution Middleware intercepts the request. |
| 2 | Verify the middleware extracts "acme" from the Host header | Subdomain is correctly parsed by stripping the platform base domain. |
| 3 | Verify the middleware checks Redis cache first (`t:subdomain:acme`) | Cache lookup is performed. |
| 4 | If cache miss, verify the middleware queries PostgreSQL for the tenant | DB query returns the acme tenant record. |
| 5 | Verify the cache is populated with the tenant data (TTL ~5 minutes) | Redis key `t:subdomain:acme` is set with serialized tenant DTO. |
| 6 | Verify `ITenantContext` is populated correctly | `TenantId` matches acme UUID, `Subdomain` = "acme", `Status` = Active, `IsSystemContext` = false, `EnabledModules` populated. |
| 7 | Verify downstream request processing is tenant-scoped | All queries include the tenant_id filter. |
| 8 | Verify all log entries include `tenant_id` via Serilog enricher | Logs contain the acme tenant_id. |
| 9 | Send a second request to the same subdomain | Cache hit resolves in <= 5ms. |
| 10 | Verify the login page displays tenant branding (logo, primary color) | Branding data is loaded from the resolved tenant profile. |
| 11 | Test with port in Host header (dev environment): `acme.localhost:5001` | Subdomain "acme" is correctly extracted despite the port. |

## 6. Postconditions
- The tenant is resolved and ITenantContext is populated for the request pipeline.
- Redis cache contains the tenant lookup result.
- All downstream operations are scoped to the resolved tenant.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
