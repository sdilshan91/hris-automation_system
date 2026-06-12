---
id: TC-CHR-ISO-028
user_story: US-CHR-007
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-028: Cache keys for locations are tenant-scoped

## 1. Test Objective
Verify that any caching layer used for location data includes the tenant_id in the cache key, preventing Tenant A from reading Tenant B's cached location data. A cache hit for one tenant must never serve data from another tenant's cache entry. This validates NFR-2.

## 2. Related Requirements
- User Story: US-CHR-007
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" (tenant_id = UUID-A) exists with locations: "Colombo Office".
- Tenant "globex" (tenant_id = UUID-B) exists with locations: "New York HQ".
- Caching layer (Redis or in-memory) is active for location data.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme (UUID-A) | Has Colombo Office |
| Tenant B | globex (UUID-B) | Has New York HQ |
| Cache Implementation | Redis or in-memory | Location data cache |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in "acme" and request `GET /api/v1/tenant/locations` | Response returns acme's locations. If caching is enabled, the result is cached. |
| 2 | Inspect the cache key used for the acme location list | The cache key includes the tenant_id (e.g., `locations:list:{UUID-A}` or `tenant:{UUID-A}:locations`). It is NOT a generic key like `locations:list`. |
| 3 | Authenticate as HR Officer in "globex" and request `GET /api/v1/tenant/locations` | Response returns globex's locations. A separate cache entry is created. |
| 4 | Inspect the cache key used for the globex location list | The cache key includes UUID-B (e.g., `locations:list:{UUID-B}`). It is different from acme's cache key. |
| 5 | Flush acme's cache entry and re-request from acme | Cache miss triggers a fresh database query. Only acme locations are returned and re-cached. Globex's cache entry remains unaffected. |
| 6 | Verify that requesting single location by ID also uses tenant-scoped cache keys | Cache key for `GET /api/v1/tenant/locations/{id}` includes tenant_id (e.g., `locations:{UUID-A}:{location-id}`). |
| 7 | Attempt to craft a cache key for globex's location while in acme context | The cache lookup uses the resolved tenant context, not user-supplied parameters. Even if an attacker guesses globex's cache key pattern, the application-level cache lookup always uses the ITenantContext tenant_id. |

## 6. Postconditions
- Cache entries are fully tenant-scoped.
- No cross-tenant cache data leakage occurred.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
