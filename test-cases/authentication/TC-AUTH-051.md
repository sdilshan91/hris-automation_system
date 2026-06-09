---
id: TC-AUTH-051
user_story: US-AUTH-007
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-09
---

# TC-AUTH-051: Reserved subdomains bypass tenant resolution

## 1. Test Objective
Verify that reserved platform subdomains are detected before tenant lookup, do not populate a regular tenant context, and route to the configured system handler.

## 2. Related Requirements
- User Story: US-AUTH-007
- Acceptance Criteria: AC-3
- Functional Requirements: FR-1, FR-3
- Business Rules: BR-2, BR-4

## 3. Preconditions
- Platform base domain is configured as `yourhrm.com`.
- Reserved subdomain list contains `www`, `api`, `app`, `mail`, `status`, `docs`, `help`, `support`, `static`, `cdn`, `dev`, `stage`, `prod`, `test`, and `qa`.
- No tenant resolution cache entry or tenant row is required for reserved subdomains.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Reserved hosts | www.yourhrm.com, api.yourhrm.com, docs.yourhrm.com | Representative reserved routes |
| Tenant cache key | t:subdomain:www | Must not be read or written |
| Expected context | No regular tenant context | System or public route handling only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET https://www.yourhrm.com/`. | Request is handled by the marketing or public site route; tenant lookup is not attempted. |
| 2 | Send `GET https://api.yourhrm.com/swagger` or equivalent API documentation route. | Request is handled by the configured system API handler; tenant lookup is not attempted. |
| 3 | Send `GET https://docs.yourhrm.com/`. | Request is handled by the configured documentation handler; tenant lookup is not attempted. |
| 4 | Inspect middleware diagnostics, logs, or mocks for Redis and PostgreSQL tenant lookup. | No `t:subdomain:{reserved}` cache read, cache write, or tenant table query occurs for any reserved host. |
| 5 | Verify `ITenantContext` after reserved host handling. | `TenantId` is empty and `IsSystemContext` is false unless the route explicitly establishes a separate system context. |
| 6 | Attempt to provision a tenant with subdomain `www` through tenant provisioning fixtures or API. | Provisioning is rejected because reserved subdomains cannot be claimed. |

## 6. Postconditions
- Reserved subdomains remain unavailable for tenant provisioning.
- No tenant cache entries are created for reserved subdomains.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
