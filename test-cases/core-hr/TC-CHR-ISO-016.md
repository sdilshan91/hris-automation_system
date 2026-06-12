---
id: TC-CHR-ISO-016
user_story: US-CHR-002
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-016: Cache keys for employee profiles are tenant-scoped

## 1. Test Objective
Verify that any caching of employee profile data uses tenant-scoped cache keys, preventing a cache entry from one tenant from being served to another tenant. This validates NFR-3.

## 2. Related Requirements
- User Story: US-CHR-002
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" and "globex" both exist.
- Caching is enabled (Redis or in-memory).
- Employee "Jane Doe" exists in acme. Employee "Eve Rogers" exists in globex.
- Both employees happen to have the same UUID format (or the test uses explicit cache key inspection).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | tenant_id = {acme_id} |
| Tenant B | globex | tenant_id = {globex_id} |
| Acme Employee | Jane Doe ({jane_id}) | Cached profile |
| Globex Employee | Eve Rogers ({eve_id}) | Cached profile |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As HR Officer in acme, request `GET /api/v1/tenant/employees/{jane_id}` | Response is 200 OK with Jane Doe's data. Profile may be cached. |
| 2 | Inspect cache (Redis or in-memory) for the cache key | Cache key includes tenant_id or tenant subdomain as a prefix/segment, e.g., `tenant:{acme_id}:employee:{jane_id}` or similar. |
| 3 | As HR Officer in globex, request `GET /api/v1/tenant/employees/{jane_id}` | Response is 404 Not Found (Jane is not in globex). The acme-scoped cache entry was NOT returned to globex. |
| 4 | As HR Officer in globex, request `GET /api/v1/tenant/employees/{eve_id}` | Response is 200 OK with Eve Rogers' data. |
| 5 | Inspect cache for Eve's entry | Cache key includes globex tenant_id, e.g., `tenant:{globex_id}:employee:{eve_id}`. |
| 6 | Verify that cache keys for acme and globex are distinct even if the employee_id UUIDs were identical | Tenant prefix in the key prevents collision. |

## 6. Postconditions
- Cache entries are correctly tenant-scoped.
- No cross-tenant cache leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
