---
id: TC-PRF-ISO-028
user_story: US-PRF-007
module: Performance Management
priority: high
type: security
status: fail
created: 2026-06-16
exec_note: "2026-06-30 API iso-fixture probe: BUG-003 cross-tenant leak CONFIRMED on performance — isoa-admin token + X-Tenant-Subdomain:isob honors the HEADER tenant: a cycle seeded in isoa (ISOA-Cycle-FP) is INVISIBLE under the isob header ([]) while visible under isoa, and dashboard/overview returns 'no_cycle for this tenant' under isob. Tenant context is header-driven, not from the JWT. Cache/export/Hangfire-job scoping fails by the same mechanism. Systemic (BUG-003 class)."
---

# TC-PRF-ISO-028: Dashboard aggregate caches + export artifacts + Hangfire materialized-view refresh jobs are tenant-scoped

## 1. Test Objective
Verify NFR-2 + NFR-3 + BR-4: the dashboard's cache keys (aggregate / overview / trend caches, Redis if wired), generated export artifacts, and the Hangfire materialized-view refresh jobs are all tenant-scoped, so no aggregate, cached value, export file, or scheduled-refresh output is shared or readable across tenants. Written as conditional where the cache layer / Hangfire schedule is an extension point.

## 2. Related Requirements
- User Story: US-PRF-007
- Non-Functional Requirements: NFR-2, NFR-3 (Redis caching / materialized views)
- Business Rules: BR-4 (Hangfire materialized-view refresh, tenant-configurable interval)

## 3. Preconditions
- Tenant "acme" and Tenant "globex" both have dashboards + populated performance_summary views.
- A cache layer (Redis) is available -- CONDITIONAL: if aggregates are computed on demand today, the test asserts tenant-filtered queries with no shared/global key and documents the cache as an extension point.
- A Hangfire recurring materialized-view refresh job is registered (CONDITIONAL: assert the refresh seam if not yet scheduled).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | own cache keys / exports / refresh job |
| Tenant B | globex | own cache keys / exports / refresh job |
| Cache key shape | includes tenant id | e.g. perf:dashboard:{tenantId}:{cycleId} |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load acme's dashboard to populate the aggregate cache; inspect cache keys | Every cache key is namespaced by acme's tenant id (no global/shared key holding acme aggregates); CONDITIONAL on a cache layer being wired. |
| 2 | Load globex's dashboard; inspect cache keys | globex aggregates are stored under globex-namespaced keys; acme and globex never collide or share an entry. |
| 3 | Update an acme review + refresh acme's materialized view; re-read globex's cached dashboard | globex's cached aggregates are unaffected; the refresh invalidates/rebuilds ONLY acme's cache entries. |
| 4 | Generate an export for acme and one for globex | Export artifacts are stored/streamed per tenant; an acme export is never retrievable from a globex context (tenant-scoped storage path / stream). |
| 5 | Inspect the Hangfire refresh job(s) | The materialized-view refresh runs per tenant in tenant context (BR-4); acme's job rebuilds only acme's summary; globex's job only globex's; the configurable interval is per tenant. |
| 6 | Confirm cross-tenant cache read is impossible | No request in globex context can read an acme-namespaced cache entry or export artifact, and vice versa (NFR-2). |

## 6. Postconditions
- Aggregate caches, export artifacts, and materialized-view refresh jobs are tenant-scoped with no cross-tenant sharing or leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
