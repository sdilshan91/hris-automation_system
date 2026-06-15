---
id: TC-PAY-ISO-004
user_story: US-PAY-001
module: Payroll
priority: high
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-004: Payroll list caches are tenant-scoped (no cross-tenant cache leak)

## 1. Test Objective
Verify AC-6 / NFR-1: the Redis cache for salary component and structure lists is keyed per tenant (e.g. `tenant:{tenantId}:payroll:components`). A cached acme list is never served to a globex request and vice versa, and a write in one tenant invalidates only that tenant's cache entry — preventing cross-tenant data leakage through a shared cache key.

## 2. Related Requirements
- User Story: US-PAY-001
- Acceptance Criteria: AC-6
- Functional Requirements: FR-8
- Non-Functional Requirements: NFR-1

## 3. Preconditions
- Tenant "acme" and "globex" each have distinct component lists.
- Redis available; caching enabled (NFR-1, 15-min TTL).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme cache key | tenant:{acme_id}:payroll:components | expected scope |
| globex cache key | tenant:{globex_id}:payroll:components | expected scope |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme, `GET .../components` to populate the cache | acme list cached under an acme-scoped key; inspect Redis to confirm the key includes acme's tenant id. |
| 2 | As globex, `GET .../components` | globex receives ONLY globex components — never a cache hit on acme's entry. globex cached under its own key. |
| 3 | Inspect Redis keys | Separate keys per tenant; no shared/global key that could serve one tenant's list to another (NFR-1). |
| 4 | As acme, create/update a component (write) | Only the acme cache entry is invalidated; globex's cached entry is untouched and still valid. |
| 5 | Re-read as acme then as globex | acme reflects the change (fresh); globex still returns its own unchanged list from cache. |

## 6. Postconditions
- Caches are strictly tenant-scoped; no cross-tenant leak via shared keys; writes invalidate only the owning tenant.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
