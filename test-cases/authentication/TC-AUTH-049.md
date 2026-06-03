---
id: TC-AUTH-049
user_story: US-AUTH-006
module: Authentication
priority: high
type: performance
status: draft
created: 2026-06-03
---

# TC-AUTH-049: Permission evaluation adds no more than 5ms overhead per request

## 1. Test Objective
Verify that the authorization middleware's permission evaluation (reading cached JWT claims and checking against required permissions) adds no more than 5ms of overhead per request, as required by NFR-1. This validates that permissions cached in JWT claims enable fast authorization decisions without database round-trips on every request.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-4 (authorization middleware evaluates request)
- Functional Requirements: FR-5
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" is provisioned and in `active` state.
- User `perf-user@acme.com` is authenticated with multiple roles (e.g., Employee, Manager, and a custom role with 50+ permissions) to test with a realistic permission set.
- The application is running in a production-like environment (not debug mode).
- Server-side instrumentation or APM tooling is available to measure authorization middleware execution time.
- Redis cache is warm with the user's permission data.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Test user | perf-user@acme.com | Multiple roles, 50+ permissions |
| JWT permissions count | 50+ | Realistic upper-range permission set |
| Test endpoints | Various requiring different permissions | Mix of permitted and denied |
| Measurement tool | Application Insights / custom middleware timing / APM | Sub-ms precision |
| Iterations | 1000 requests | For statistical significance |
| Acceptable overhead | <= 5ms P95 | Per NFR-1 |
| Tenant | acme (acme.yourhrm.com) | Active tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `perf-user@acme.com` and obtain JWT with 50+ permissions. | JWT issued successfully. Permission claim contains 50+ entries. |
| 2 | Enable server-side timing instrumentation for the authorization middleware (e.g., `Server-Timing` header or APM spans). | Instrumentation active. The authorization middleware execution time is measurable separately from business logic. |
| 3 | Send 1000 sequential requests to `GET /api/v1/tenant/leave/requests` (a permitted endpoint) with the JWT. Record authorization middleware time for each request. | All requests return HTTP 200 OK. Authorization middleware time is recorded per request. |
| 4 | Calculate P50, P95, and P99 of authorization middleware execution time. | P95 <= 5ms. P50 should be significantly below 5ms (typically < 1ms for in-memory JWT claim checks). |
| 5 | Send 1000 sequential requests to `GET /api/v1/tenant/payroll/runs` (a denied endpoint) with the same JWT. Record authorization middleware time. | All requests return HTTP 403 Forbidden. Authorization middleware time is recorded. |
| 6 | Calculate P50, P95, and P99 for the denied requests. | P95 <= 5ms. Denied requests should not take significantly longer than permitted requests (both are claim lookups). |
| 7 | Compare JWT with 5 permissions vs. 50 permissions vs. 200 permissions. Send 100 requests for each. | Authorization overhead scales sub-linearly with permission count. Even with 200 permissions, P95 remains <= 5ms. |
| 8 | Verify no database query is triggered during permission evaluation for standard requests. | Database query logs show zero role/permission queries during the 1000-request test (all evaluation is from JWT claims and/or Redis cache). |
| 9 | Verify the total request overhead (authorization + routing + serialization) does not push read endpoints beyond the 400ms P95 SLA. | End-to-end P95 for the read endpoint is <= 400ms, with authorization contributing <= 5ms. |

## 6. Postconditions
- Performance metrics are collected and documented.
- No degradation in authorization performance under sustained load.
- Results confirm JWT-based permission evaluation meets the 5ms SLA.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
