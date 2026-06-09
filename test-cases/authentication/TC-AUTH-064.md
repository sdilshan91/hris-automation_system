---
id: TC-AUTH-064
user_story: US-AUTH-008
module: Authentication
priority: medium
type: performance
status: draft
created: 2026-06-09
---

# TC-AUTH-064: My-tenants cache performance and invalidation

## 1. Test Objective
Verify that `my-tenants` uses a per-user Redis cache with acceptable performance, correct tenant isolation, and invalidation after membership changes.

## 2. Related Requirements
- User Story: US-AUTH-008
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-9
- Non-Functional Requirements: NFR-1, NFR-2, NFR-3, NFR-4
- Business Rules: BR-5

## 3. Preconditions
- Redis is available and configured for the authentication service.
- User `multi.user@yourhrm.test` has memberships in tenants A and B.
- A tenant admin or test fixture can add, remove, suspend, or terminate a membership.
- Performance tooling can capture P95 latency over repeated requests.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Cache key pattern | user:{userId}:tenants | Per story requirement |
| Request volume | 100 requests | Warm-cache P95 sample |
| Switch SLA | <= 400 ms P95 | Token generation and verification |
| Cache scenario | Add tenant C, suspend tenant B | Invalidation checks |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Clear the user's `my-tenants` cache key. | No stale cached tenant list remains. |
| 2 | Call `GET /api/v1/auth/my-tenants`. | HTTP 200 returns fresh memberships and populates Redis using `user:{userId}:tenants`. |
| 3 | Repeat `my-tenants` 100 times for the same user. | P95 response time meets the read SLA and uses cache hits where observable. |
| 4 | Authenticate a different user with memberships in different tenants and call `my-tenants`. | Different user's response and cache entry do not include the first user's tenants. |
| 5 | Add tenant C membership for the first user. | Membership-change workflow invalidates or refreshes the cached list. |
| 6 | Call `my-tenants` again. | Tenant C appears without waiting for stale cache expiration. |
| 7 | Suspend or terminate tenant B or change membership status. | Cache is invalidated or refreshed. |
| 8 | Call `my-tenants` again. | Tenant B still appears when membership exists, with updated non-accessible status and no stale role/status values. |
| 9 | Execute 100 tenant switch requests to tenant B or tenant C where accessible. | P95 switch response time is <= 400 ms including membership verification and token issuance. |
| 10 | Repeat a cache observation behind two API instances or simulated load-balanced requests. | Both instances return consistent tenant lists and do not rely on in-memory-only state. |

## 6. Postconditions
- Cache state reflects current memberships and statuses.
- No cross-user or cross-tenant cache leakage is observed.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
