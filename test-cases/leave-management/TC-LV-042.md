---
id: TC-LV-042
user_story: US-LV-002
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-13
---

# TC-LV-042: Redis cache for leave balances with 24h TTL and event-driven invalidation (DEFERRED)

## 1. Test Objective
Verify that computed leave balances are cached in Redis with the key pattern `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`, a TTL of 24 hours, and event-driven invalidation on write operations (entitlement rule changes, override changes, leave usage, adjustments).

**DEFERRED:** This test case is deferred because Redis caching for leave balances is not yet implemented. The codebase has `IDistributedCache` registered but no entity currently uses a cache layer (see `docs/vault/modules/leave-management.md` -- caching decision deferred). When the tenant-scoped caching pattern is established, this test case should be activated.

## 2. Related Requirements
- User Story: US-LV-002
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-6

## 3. Preconditions
- **DEFERRED** -- Redis caching must be implemented for leave balances.
- Redis server is running and accessible.
- Tenant "acme" exists with employees and leave balances.

## 4. Test Data
| Parameter | Value | Notes |
|-----------|-------|-------|
| Cache Key Pattern | tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId} | FR-6 |
| TTL | 24 hours (86,400 seconds) | NFR-3 |
| Tenant ID | acme_tenant_id | UUID |
| Employee ID | jane_smith_id | UUID |
| Leave Type ID | annual_leave_id | UUID |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **DEFERRED** -- Query leave balance for Jane Smith, Annual Leave via API | Response returned. Verify a Redis key `tenant:{acme_id}:leave_balance:{jane_id}:{annual_id}` was created. |
| 2 | **DEFERRED** -- Inspect the Redis key TTL | TTL = ~86,400 seconds (24 hours). |
| 3 | **DEFERRED** -- Query the same balance again | Response served from cache (faster response, no DB query logged). |
| 4 | **DEFERRED** -- Update an entitlement rule affecting Jane | The cache key for Jane + Annual Leave is invalidated (deleted from Redis). |
| 5 | **DEFERRED** -- Query Jane's balance again | Fresh computation from DB; new cache entry created with 24h TTL. |
| 6 | **DEFERRED** -- Set a per-employee override for Jane | Cache key invalidated. |
| 7 | **DEFERRED** -- Verify a leave usage event (booking leave) also invalidates the cache | Cache key for the affected employee + leave type is invalidated. |
| 8 | **DEFERRED** -- Verify cache keys are tenant-scoped: Tenant A cache does not serve Tenant B | Different tenant IDs produce different cache keys; no cross-tenant cache leakage. |

## 6. Postconditions
- **DEFERRED** -- Cache operates with 24h TTL and event-driven invalidation.
- No stale data served after write operations.
- Cache keys are tenant-scoped preventing cross-tenant leakage.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [x] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
