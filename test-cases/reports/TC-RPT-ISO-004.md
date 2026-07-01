---
id: TC-RPT-ISO-004
user_story: US-RPT-001
module: Reports & Analytics
priority: high
type: security
status: fail
exec_note: "2026-06-30 API iso-fixture probe: report cache key NOT tenant-isolated — isoa-tok+isob-hdr headcount returned isob's cached result (generatedAt reused from isob own call). Cross-tenant cache collision. BUG-003 class (ISSUE-193)."
created: 2026-06-17
---

# TC-RPT-ISO-004: Report cache keys are tenant-scoped; no cross-tenant cache collision or leakage (FR-5, NFR-2)

## 1. Test Objective
Verify the Redis report cache keys embed the tenant id (`t:{tenantId}:report:{name}:{paramsHash}`)
so two tenants generating the SAME report type with the SAME filter parameters produce DISTINCT
cache entries and can never read each other's cached results. Validates FR-5 key shape under the
isolation lens (NFR-2).

> PLATFORM NOTE / CONDITIONAL: Redis may be deferred infra on the dev box (as in prior modules).
> If Redis is wired, assert the key shape and isolation directly. If not, assert that the
> caching layer's key-derivation function includes `t:{tenantId}` for the active tenant so the
> isolation property holds once caching is enabled — and that results are always recomputed
> per-tenant in the meantime.

## 2. Related Requirements
- User Story: US-RPT-001
- Acceptance Criteria: AC-5
- Functional Requirements: FR-5 (Redis cache key shape), FR-7
- Non-Functional: NFR-2 (tenant isolation)
- Dependencies: Redis

## 3. Preconditions
- Tenant A and Tenant B active with distinct populations.
- `hrA` and `hrB` authenticated in their own tenants.
- Redis available (else CONDITIONAL per the note).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| report_type | headcount_summary | same in both tenants |
| params | dept=Engineering, current month | identical params in both tenants |
| expected key A | t:{TenantA}:report:headcount_summary:{hash} | |
| expected key B | t:{TenantB}:report:headcount_summary:{hash} | same hash, different tenant prefix |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `hrA`, generate the report with the given params | Cache entry created at `t:{TenantA}:report:headcount_summary:{paramsHash}` |
| 2 | As `hrB`, generate the report with the IDENTICAL params | A SEPARATE cache entry created at `t:{TenantB}:report:...{same paramsHash}` — the tenant prefix differs even though the params hash matches |
| 3 | Inspect both Redis keys | Keys differ ONLY by the `t:{tenantId}` segment; B never reads A's entry and vice versa |
| 4 | Warm A's cache, then have B request the same params | B is a cache MISS on its own key (recomputes B's data); B does not get A's 43-count result |
| 5 | Compare cached payloads | A's cached result = A's data; B's = B's data; no collision despite identical params hash |
| 6 | Expire/refresh A's entry (Refresh per FR-8) | Only A's key is affected; B's entry untouched |

## 6. Postconditions
- Cache keys tenant-prefixed; no cross-tenant cache collision or read.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
