---
id: TC-LV-016
user_story: US-LV-001
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-13
---

# TC-LV-016: Leave type list API response within 200ms P95

## 1. Test Objective
Verify that the leave type list API endpoint responds within 200ms at the P95 percentile, as specified in NFR-1. Test with Redis caching enabled (if implemented) and after cache invalidation on write.

## 2. Related Requirements
- User Story: US-LV-001
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" exists with 15-20 leave types configured (realistic max).
- A user with `Leave.Configure` permission is authenticated.
- Redis is running (if caching is implemented; otherwise mark cache-specific steps DEFERRED).

## 4. Test Data
| Parameter | Value | Notes |
|-----------|-------|-------|
| Tenant | acme | 15-20 leave types |
| Request Count | 100 | For P95 calculation |
| P95 Target | 200ms | NFR-1 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/leave-types` 100 times with valid authentication | All requests return 200 OK with correct data. |
| 2 | Record response times for all 100 requests | Response times collected. |
| 3 | Calculate P95 response time | P95 <= 200ms. |
| 4 | Verify first request (cold cache) is within acceptable range | First request may be slower but should still be under 400ms. |
| 5 | Verify subsequent requests benefit from Redis cache (DEFERRED if caching not implemented) | Cached responses faster than uncached. Cache hit header or reduced DB query count observed. |
| 6 | Update a leave type (write operation) | 200 OK. Cache invalidated. |
| 7 | Immediately send `GET /api/v1/leave-types` | Response contains the updated data (fresh, not stale cache). Response time still within acceptable range. |

## 6. Postconditions
- API performance meets NFR-1 SLA.
- Cache invalidation on write ensures data freshness.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
