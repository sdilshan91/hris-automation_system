---
id: TC-CHR-ISO-040
user_story: US-CHR-010
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-ISO-040: Cache keys for bulk import operations and imported data are tenant-scoped

## 1. Test Objective
Verify that any caching used during or after bulk import (e.g., employee count cache, department lookup cache, job title lookup cache, import progress cache) is scoped to the tenant. Tenant A's cache entries must not leak to Tenant B and vice versa.

## 2. Related Requirements
- User Story: US-CHR-010
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant A ("acme") and Tenant B ("globex") exist.
- Both tenants have departments and job titles with the SAME names (e.g., both have "Engineering").
- Caching infrastructure is active (in-memory or Redis).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A Subdomain | acme.yourhrm.com | Has department "Engineering" (acme-eng-uuid) |
| Tenant B Subdomain | globex.yourhrm.com | Has department "Engineering" (globex-eng-uuid) |
| Tenant A File | acme_import.csv | Rows reference "Engineering" |
| Tenant B File | globex_import.csv | Rows reference "Engineering" |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Import `acme_import.csv` in Tenant A. The system caches department lookups during processing. | Employees created with `department_id = acme-eng-uuid`. |
| 2 | Immediately import `globex_import.csv` in Tenant B. | Employees created with `department_id = globex-eng-uuid` (NOT acme-eng-uuid). The department lookup cache for "Engineering" did not cross-contaminate between tenants. |
| 3 | Verify cache key format (if accessible via inspection or logging). | Cache keys include the tenant identifier, e.g., `import:dept-lookup:{tenantId}:Engineering` or equivalent tenant-scoped pattern. |
| 4 | If import progress is cached (for async jobs), verify that Tenant A's progress is not visible via Tenant B's session. | Querying import progress from Tenant B returns only Tenant B's import jobs, not Tenant A's. |

## 6. Postconditions
- Cache isolation confirmed. No cross-tenant cache leakage during import operations.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
