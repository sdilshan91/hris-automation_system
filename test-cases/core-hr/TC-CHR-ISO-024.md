---
id: TC-CHR-ISO-024
user_story: US-CHR-006
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-024: Cache keys for org-tree data are tenant-scoped

## 1. Test Objective
Verify that any caching layer used for org-tree data (e.g., Redis, in-memory cache) includes the tenant_id in the cache key, preventing Tenant A from receiving Tenant B's cached org-tree data. This validates NFR-3.

## 2. Related Requirements
- User Story: US-CHR-006
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-8

## 3. Preconditions
- Tenant "acme" (tenant_id: AAA-111) and Tenant "globex" (tenant_id: BBB-222) exist.
- Both tenants have departments and employees.
- Caching is enabled for org-tree API responses (if applicable).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme (AAA-111) | First request populates cache |
| Tenant B | globex (BBB-222) | Second request should NOT get acme cache |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Tenant A HR Officer, send `GET /api/v1/org-tree?view=department&depth=2` | Response contains acme departments. If caching is active, the result is cached. |
| 2 | Inspect the cache key (via Redis CLI, logs, or test instrumentation) | The cache key includes the tenant_id or tenant subdomain (e.g., `org-tree:AAA-111:department:depth-2` or similar tenant-scoped pattern). |
| 3 | As Tenant B HR Officer, send `GET /api/v1/org-tree?view=department&depth=2` | Response contains ONLY globex departments. Tenant A's cached data is NOT returned. |
| 4 | Inspect the cache key for Tenant B | The cache key includes Tenant B's tenant_id (e.g., `org-tree:BBB-222:department:depth-2`), distinct from Tenant A's key. |
| 5 | Verify the two cache entries are separate | If Redis is used, `KEYS org-tree:*` shows two separate entries with different tenant prefixes. |
| 6 | Invalidate Tenant A's cache (e.g., by updating a department) | Tenant A's cache entry is cleared. Tenant B's cache entry remains unaffected. |
| 7 | Re-request Tenant A's org tree | Fresh data is fetched from the database and re-cached. Tenant B's cached data is still separate. |

## 6. Postconditions
- Cache isolation between tenants is confirmed.
- No cross-tenant cache contamination occurred.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
