---
id: TC-PAY-ISO-024
user_story: US-PAY-006
module: Payroll
priority: high
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-024: Statutory-rule Redis cache is tenant-scoped; a write in one tenant invalidates only that tenant and never leaks rules to another tenant

## 1. Test Objective
Verify AC-4 / FR-8 / NFR-1: the statutory-rules Redis cache (used to meet the <10ms calc SLA, NFR-2) is keyed per tenant (e.g. `tenant:{tenantId}:payroll:statutory:{fiscalYear}`) with no shared/global key. Tenant A's cached rules are never served to Tenant B, and a write in Tenant A invalidates only Tenant A's cache entry -- Tenant B's cached rules are unaffected. If no Redis layer exists today, the test asserts the resolver always uses tenant-filtered queries with no shared cache key.

## 2. Related Requirements
- User Story: US-PAY-006
- Acceptance Criteria: AC-4
- Functional Requirements: FR-8
- Non-Functional Requirements: NFR-1 (Redis cache + invalidation on write), NFR-2 (<10ms calc)
- Data Requirements: S7

## 3. Preconditions
- Tenant A "acme" and Tenant B "globex" each with active statutory rules.
- Redis available; cache key pattern `tenant:{tenantId}:payroll:statutory:{fiscalYear}`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A key | tenant:{acmeId}:payroll:statutory:2025-2026 | scoped |
| Tenant B key | tenant:{globexId}:payroll:statutory:2025-2026 | scoped |
| Shared/global key | (none) | must not exist |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Warm both tenants' statutory caches by resolving rules in each. | Two distinct tenant-scoped Redis keys exist; no shared/global statutory key is created. |
| 2 | As globex, resolve rules and inspect the served values. | globex receives only globex's rules from its own key; acme's cached rules are never returned. |
| 3 | Update an acme tax slab (write). | Only acme's cache key is invalidated/refreshed; globex's cached rules and key are untouched (NFR-1). |
| 4 | Re-resolve in acme then in globex. | acme reflects the new slab (fresh); globex still serves its unchanged rules from cache -- no cross-tenant invalidation or value bleed. |
| 5 | Attempt to read acme's cache key while in globex context. | Not possible through the application; the resolver derives the key from the resolved tenant only -- globex cannot target acme's key. |
| 6 | If no Redis layer exists today (CONDITIONAL). | Assert the resolver uses tenant-filtered DB queries with no shared/global cache key; the cache-invalidation step is deferred to when Redis is enabled, but the no-shared-key guarantee still holds. |

## 6. Postconditions
- Statutory caches are strictly tenant-scoped; writes invalidate only the writing tenant; no cross-tenant cache leak.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
