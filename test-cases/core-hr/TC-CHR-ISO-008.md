---
id: TC-CHR-ISO-008
user_story: US-CHR-005
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-008: Cache keys for job titles are tenant-scoped

## 1. Test Objective
Verify that any caching of job title data (e.g., Redis cache, in-memory cache) uses tenant-scoped cache keys, preventing one tenant from reading another tenant's cached job title data.

## 2. Related Requirements
- User Story: US-CHR-005
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" (Tenant A) and Tenant "globex" (Tenant B) exist with status `active`.
- Caching is enabled for job title data (if the implementation uses caching).
- Both tenants have job titles.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has cached job titles |
| Tenant B | globex | Has cached job titles |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Tenant A admin, call `GET /api/v1/job-titles` to populate the cache | Job titles for acme are cached. |
| 2 | Inspect the cache key structure (via Redis CLI or cache monitoring) | Cache key includes the tenant identifier, e.g., `job_titles:tenant:{uuid_a}:list` or similar tenant-scoped pattern. |
| 3 | As Tenant B admin, call `GET /api/v1/job-titles` | Response returns only Tenant B's job titles (NOT Tenant A's cached data). |
| 4 | Verify Tenant B's cache key is different from Tenant A's | Cache key includes Tenant B's identifier: `job_titles:tenant:{uuid_b}:list`. |
| 5 | Clear Tenant A's cache and verify Tenant B's cache is unaffected | Tenant B can still retrieve cached data without re-fetching from the database. |
| 6 | If no caching is implemented for job titles, document this and mark the test as N/A | Test is not applicable if no caching layer exists for this feature. |

## 6. Postconditions
- Cache keys are confirmed to be tenant-scoped.
- No cross-tenant cache pollution is possible.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
