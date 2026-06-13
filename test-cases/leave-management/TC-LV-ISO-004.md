---
id: TC-LV-ISO-004
user_story: US-LV-001
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-004: Cache keys for leave types are tenant-scoped

## 1. Test Objective
Verify that Redis cache keys for leave type data are scoped by tenant_id, preventing Tenant A from receiving Tenant B's cached leave type data. **Status: Cache-specific steps DEFERRED if Redis caching is not yet implemented for leave types.**

## 2. Related Requirements
- User Story: US-LV-001
- Non-Functional Requirements: NFR-1, NFR-2

## 3. Preconditions
- Tenant "acme" and "globex" each have leave types configured.
- Redis is running and caching is enabled for leave types (DEFERRED if not implemented).
- Both tenants' leave type lists have been recently fetched (cache populated).

## 4. Test Data
| Tenant | Expected Cache Key Pattern | Notes |
|--------|---------------------------|-------|
| acme | `leave_types:acme_uuid:*` or `tenant:{acme_uuid}:leave_types` | Tenant-scoped |
| globex | `leave_types:globex_uuid:*` or `tenant:{globex_uuid}:leave_types` | Tenant-scoped |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme user, `GET /api/v1/leave-types` to populate cache | Response returned; cache entry created with acme's tenant_id in the key. (DEFERRED if no caching) |
| 2 | As globex user, `GET /api/v1/leave-types` to populate cache | Response returned; separate cache entry created with globex's tenant_id in the key. |
| 3 | Inspect Redis keys: `KEYS *leave_type*` or equivalent | Cache keys include tenant_id segment. Acme and globex keys are distinct. |
| 4 | As acme user, `GET /api/v1/leave-types` (cached response) | Returns only acme's leave types, not globex's. Cache hit does not leak cross-tenant data. |
| 5 | Update an acme leave type | Acme's cache key is invalidated. Globex's cache key remains valid. |
| 6 | As acme user, `GET /api/v1/leave-types` (after invalidation) | Returns fresh data from DB. New cache entry created. Globex data still not included. |

## 6. Postconditions
- Cache keys are verifiably tenant-scoped.
- No cache-based cross-tenant data leakage occurred.
- Cache invalidation is tenant-specific (does not flush other tenants' caches).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
