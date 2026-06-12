---
id: TC-CHR-ISO-044
user_story: US-CHR-011
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-044: Cache keys for reporting structure and direct-reports are tenant-scoped

## 1. Test Objective
Verify that any caching of reporting structure data (direct-reports lists, cycle detection lookups) uses tenant-scoped cache keys. A cache populated by Tenant A's data must not be served to Tenant B. This validates NFR-3.

## 2. Related Requirements
- User Story: US-CHR-011
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" and Tenant "globex" both exist with status `active`.
- Manager A in Tenant "acme" has 3 direct reports.
- Manager G in Tenant "globex" has 2 direct reports.
- Caching is enabled in the application (if implemented).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Manager A: 3 direct reports |
| Tenant B | globex | Manager G: 2 direct reports |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer in Tenant A. Query Manager A's direct reports twice. | First call populates cache. Second call returns from cache. Both return 3 employees. |
| 2 | Authenticate as HR Officer in Tenant B. Query Manager G's direct reports. | Returns 2 employees (Tenant B data). Does NOT return Tenant A's cached 3 employees. |
| 3 | If cache introspection is available: verify cache keys include the tenant identifier. | Cache keys follow pattern like `tenant:{tenantId}:employee:{managerId}:direct-reports` or equivalent tenant-scoped pattern. |
| 4 | In Tenant A, assign a new direct report to Manager A. Query direct reports again. | Returns 4 employees. Cache is invalidated/updated correctly for Tenant A only. |
| 5 | In Tenant B, verify Manager G still shows 2 direct reports. | Tenant A's cache invalidation does not affect Tenant B's cache. |

## 6. Postconditions
- Cache isolation confirmed. No cross-tenant data leakage via caching layer.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
