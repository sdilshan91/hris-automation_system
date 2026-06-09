---
id: TC-AUTH-055
user_story: US-AUTH-007
module: Authentication
priority: high
type: performance
status: draft
created: 2026-06-09
---

# TC-AUTH-055: Tenant resolution cache hit completes within 5 ms

## 1. Test Objective
Verify that tenant resolution uses Redis on cache hit and adds no more than 5 ms overhead at P95.

## 2. Related Requirements
- User Story: US-AUTH-007
- Acceptance Criteria: AC-1
- Functional Requirements: FR-5, FR-6, FR-9
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant `acme` exists with active status.
- Redis is running and reachable.
- Redis key `t:subdomain:acme` is pre-populated with the serialized tenant resolution DTO.
- Performance instrumentation can measure tenant resolution middleware elapsed time independently from controller execution.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Host | acme.yourhrm.com | Active cached tenant |
| Cache key | t:subdomain:acme | Pre-populated |
| Sample size | 1,000 requests | Warm-up excluded |
| SLA | P95 <= 5 ms | Tenant resolution overhead only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Warm up the API and Redis connection pool. | Initial connection overhead is excluded from measurement. |
| 2 | Send 1,000 requests to a lightweight tenant endpoint on `acme.yourhrm.com`. | Every request resolves `acme` from Redis. |
| 3 | Verify Redis and PostgreSQL call counts. | Redis GET is called for each measured request; PostgreSQL tenant lookup is not called. |
| 4 | Calculate P50, P95, and P99 middleware resolution overhead. | P95 is <= 5 ms. P99 is reviewed for outliers. |
| 5 | Verify resolved context on sampled requests. | `ITenantContext` contains acme tenant ID, status, plan, and enabled modules. |
| 6 | Verify logs and metrics. | Metrics identify cache hits and include tenant tags without leaking sensitive data. |

## 6. Postconditions
- Redis cache remains populated for `t:subdomain:acme`.
- Performance evidence is attached to the test run.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
