---
id: TC-CHR-ISO-020
user_story: US-CHR-003
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-020: Cache keys for directory queries are tenant-scoped

## 1. Test Objective
Verify that any caching mechanism used for directory queries (search results, filter options, export files) uses tenant-scoped cache keys. Tenant A's cached directory data must not be served to Tenant B.

## 2. Related Requirements
- User Story: US-CHR-003
- Functional Requirements: FR-7
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with 30 employees.
- Tenant "globex" exists with 20 employees.
- Both tenants have HR Officers authenticated.
- Redis/in-memory cache is active.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A cache key pattern | `directory:acme:*` | Tenant-prefixed |
| Tenant B cache key pattern | `directory:globex:*` | Tenant-prefixed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As HR Officer in "acme", load the directory (populates cache) | Directory shows 30 employees. Cache is populated with acme-scoped key. |
| 2 | Inspect cache keys (Redis CLI or equivalent) | Cache key includes "acme" tenant identifier or tenant UUID. |
| 3 | As HR Officer in "globex", load the directory | Directory shows 20 employees (NOT 30 from acme's cache). |
| 4 | Inspect cache keys for globex | Cache key includes "globex" tenant identifier; different from acme's key. |
| 5 | Verify that flushing acme's cache does not affect globex | After clearing acme's cache, globex directory still loads from cache (20 employees). |
| 6 | Verify export cache (if applicable) | Export download URLs or cached files are tenant-scoped. |

## 6. Postconditions
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
