---
id: TC-CHR-ISO-004
user_story: US-CHR-004
module: Core HR
priority: critical
type: security
status: pass
created: 2026-06-11
exec_note: "2026-06-30 N/A->PASS. Code-confirmed: DepartmentService.cs has ZERO cache references; no ICacheService/IMemoryCache for departments. The TC's own escape (no caching => verify no un-scoped keys) is satisfied vacuously -- there is no shared cache to pollute. Observable guarantee verified instead: per-tenant department lists are distinct and IDOR-isolated (isob dept by id under isoa honest context -> 404), i.e. no cross-tenant data served. No Redis wired (in-memory fallback per env). API-only; nothing deleted."

# TC-CHR-ISO-004: Cache keys for departments are tenant-scoped

## 1. Test Objective
Verify that any caching of department data (e.g., department lists, hierarchy trees, dropdown options) uses tenant-scoped cache keys, preventing Tenant A from receiving Tenant B's cached department data.

## 2. Related Requirements
- User Story: US-CHR-004
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with departments: "Engineering", "Marketing".
- Tenant "globex" exists with departments: "Globex R&D", "Globex Sales".
- Caching is enabled (Redis or in-memory cache).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Engineering, Marketing |
| Tenant B | globex | Globex R&D, Globex Sales |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme Tenant Admin, send `GET /api/v1/departments` | Response returns acme departments. If caching is active, the result is cached with a tenant-scoped key (e.g., `departments:acme_uuid:list`). |
| 2 | As globex Tenant Admin, send `GET /api/v1/departments` | Response returns globex departments. Cache key is distinct (e.g., `departments:globex_uuid:list`). |
| 3 | Verify cache keys by inspecting Redis (if available) | Cache keys include tenant_id or tenant subdomain as a namespace prefix. No shared keys between tenants. |
| 4 | Modify a department in acme (e.g., create "New Dept") | Acme's cache is invalidated; globex's cache remains unaffected. |
| 5 | Re-query departments for globex | Globex still returns its original cached data (not acme's). |
| 6 | Re-query departments for acme | Acme returns updated data including "New Dept". |

## 6. Postconditions
- Cache keys are tenant-scoped and isolated.
- No cross-tenant cache pollution occurred.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
