---
id: TC-CHR-ISO-048
user_story: US-CHR-012
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-CHR-ISO-048: Cache keys for custom field definitions are tenant-scoped

## 1. Test Objective
Verify that any caching of custom field definitions (configuration or rendered field sets) uses tenant-scoped cache keys. Tenant A's cached custom field configuration must never be served to Tenant B, and vice versa. This validates NFR-2.

## 2. Related Requirements
- User Story: US-CHR-012
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" and Tenant "globex" both exist.
- Both tenants have custom field definitions.
- Caching layer is enabled (in-memory or distributed).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Custom fields: T-Shirt Size, Project Code |
| Tenant B | globex | Custom fields: Office Floor |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Tenant A. Query custom fields to prime the cache. | Cache key includes tenant_id (e.g., `custom_fields:acme:{entity}` or similar tenant-scoped key). |
| 2 | Authenticate as Tenant B. Query custom fields. | A separate cache entry is created for globex. The response contains only globex's fields, not acme's cached data. |
| 3 | Update a field in Tenant A (e.g., rename). | Only Tenant A's cache is invalidated. Tenant B's cached data is unaffected. |
| 4 | Re-query Tenant A's fields. | Fresh data fetched and re-cached with tenant-scoped key. |
| 5 | Inspect cache keys (via logging, debugging, or cache admin interface). | All custom field cache keys contain the tenant identifier. No global/unscoped keys exist. |

## 6. Postconditions
- Cache isolation is verified. No cross-tenant cache leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
