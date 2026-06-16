---
id: TC-PAY-ISO-028
user_story: US-PAY-007
module: Payroll
priority: high
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-028: Adjustment list / pending-lookup caches and the document-download cache are tenant-scoped (no cross-tenant adjustment, count, or PDF/byte leak)

## 1. Test Objective
Verify AC-5 / FR-8 / NFR-1: any caching of the adjustments list, the run-engine's pending-adjustments-for-period lookup, or supporting-document downloads is keyed by tenant (e.g. `tenant:{tenantId}:payroll:adjustments:...`). A write/cancel in one tenant invalidates only that tenant's entry; no cached adjustment row, count, or document byte is served across tenants. If no cache layer exists today, the test asserts tenant-filtered DB resolution with no shared/global key (the no-shared-key guarantee still holds).

## 2. Related Requirements
- User Story: US-PAY-007
- Acceptance Criteria: AC-5
- Functional Requirements: FR-8
- Non-Functional Requirements: NFR-1
- Note: CONDITIONAL on a cache layer existing -- if adjustments are resolved on demand, assert no shared/global key + always-tenant-filtered queries.

## 3. Preconditions
- Tenant A "acme" and Tenant B "globex" each with their own adjustments; both users with `Payroll.*.All`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Cache key pattern | `tenant:{tenantId}:payroll:adjustments:*` | tenant-scoped |
| acme pending count | known N_acme | for the run period |
| globex pending count | known N_globex | distinct |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme, list adjustments / resolve pending-for-period to warm any cache. | acme entry cached under an acme-scoped key (or resolved on demand, tenant-filtered). |
| 2 | As globex, list adjustments / resolve pending-for-period. | globex sees only globex rows + N_globex; never acme's cached list/count. |
| 3 | As acme, create + then cancel an adjustment. | acme's cached list/count is invalidated and refreshed; globex's cache entry is untouched (no cross-tenant invalidation or staleness). |
| 4 | Download an acme supporting document, then as globex request the same document. | globex never receives acme's cached document bytes; download cache (if any) is tenant-scoped (FR-8). |
| 5 | Inspect the cache keys in use (or confirm no caching). | All keys carry the tenant id; no shared/global adjustment or document key exists (FR-8, NFR-1). |

## 6. Postconditions
- Adjustment/document caches are strictly tenant-scoped; no cross-tenant list, count, or byte leak; per-tenant invalidation.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
