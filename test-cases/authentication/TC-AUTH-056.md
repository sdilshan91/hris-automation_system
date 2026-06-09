---
id: TC-AUTH-056
user_story: US-AUTH-007
module: Authentication
priority: high
type: performance
status: draft
created: 2026-06-09
---

# TC-AUTH-056: Cache miss falls back to PostgreSQL and repopulates Redis within 50 ms

## 1. Test Objective
Verify that a missing or expired Redis tenant cache entry falls back to PostgreSQL, repopulates Redis with the configured TTL, and meets the cache-miss latency SLA.

## 2. Related Requirements
- User Story: US-AUTH-007
- Acceptance Criteria: AC-6
- Functional Requirements: FR-5, FR-6, FR-9
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant `acme` exists with active status and enabled modules.
- Redis is running.
- Redis key `t:subdomain:acme` is deleted or expired before the test begins.
- Configured tenant cache TTL is known; default expected value is 5 minutes.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Host | acme.yourhrm.com | Active tenant |
| Cache key | t:subdomain:acme | Deleted before first request |
| Expected TTL | 5 minutes default | Or configured equivalent |
| SLA | P95 <= 50 ms | DB lookup plus cache write |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Delete `t:subdomain:acme` from Redis. | Cache key is absent. |
| 2 | Send a request to `https://acme.yourhrm.com/api/v1/auth/login` or a lightweight tenant route. | Middleware performs Redis miss, queries PostgreSQL, and resolves acme. |
| 3 | Verify `ITenantContext`. | Context contains acme tenant ID, subdomain, active status, plan, and enabled modules. |
| 4 | Inspect Redis after the request. | `t:subdomain:acme` exists with serialized tenant resolution DTO and configured TTL. |
| 5 | Measure cache-miss tenant resolution overhead over a controlled sample. | P95 is <= 50 ms for DB lookup plus cache write. |
| 6 | Send a second request for `acme.yourhrm.com`. | Request uses the newly populated Redis entry and follows cache-hit behavior. |

## 6. Postconditions
- Redis contains a fresh tenant cache entry for `acme`.
- Cache miss metrics are available for SLA evidence.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
