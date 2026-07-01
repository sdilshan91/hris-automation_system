---
id: TC-RPT-ISO-008
user_story: US-RPT-002
module: Reports & Analytics
priority: high
type: security
status: fail
exec_note: "2026-06-30 API iso-fixture probe: report cache key NOT tenant-isolated — isoa-tok+isob-hdr headcount returned isob's cached result (generatedAt reused from isob own call). Cross-tenant cache collision. BUG-003 class (ISSUE-193)."
created: 2026-06-17
---

# TC-RPT-ISO-008: Leave/attendance report cache keys tenant-prefixed; no cross-tenant cache collision (FR-7, NFR-2)

## 1. Test Objective
Verify the Redis report cache keys embed the tenant id (`t:{tenantId}:report:{type}:{filterHash}`)
so two tenants generating the SAME leave/attendance report with the SAME filters produce DISTINCT
cache entries and can never read each other's cached results. Validates FR-7 key shape under the
isolation lens (NFR-2).

> PLATFORM NOTE / CONDITIONAL: Redis may be deferred infra on the dev box. If wired, assert the key
> shape and isolation directly. If not, assert that the cache key-derivation includes `t:{tenantId}`
> for the active tenant so isolation holds once caching is enabled — and that results are recomputed
> per-tenant in the meantime.

## 2. Related Requirements
- User Story: US-RPT-002
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7 (Redis cache key shape, tenant+type+filter-hash)
- Non-Functional: NFR-2 (tenant isolation)
- Dependencies: Redis

## 3. Preconditions
- Tenant A and Tenant B active with distinct leave/attendance data.
- `hrA` and `hrB` authenticated in their own tenants.
- Redis available (else CONDITIONAL per the note).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| report_type | leave_utilization | same in both tenants |
| filters | dept=Engineering, Q1 2026 | identical filter set in both |
| expected key A | t:{TenantA}:report:leave_utilization:{hash} | |
| expected key B | t:{TenantB}:report:leave_utilization:{hash} | same hash, different tenant prefix |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `hrA`, generate the report with the given filters | Cache entry created at `t:{TenantA}:report:leave_utilization:{filterHash}` |
| 2 | As `hrB`, generate with IDENTICAL filters | SEPARATE entry at `t:{TenantB}:report:...{same filterHash}` — tenant prefix differs despite matching hash |
| 3 | Inspect both Redis keys | Keys differ ONLY by the `t:{tenantId}` segment; B never reads A's entry and vice versa |
| 4 | Warm A's cache, then have B request the same filters | B is a cache MISS on its own key (recomputes B's data); B does not receive A's totals |
| 5 | Compare cached payloads | A's = A's data; B's = B's data; no collision despite identical filter hash |
| 6 | Refresh A's entry (FR-7 bypass) | Only A's key affected; B's entry untouched |

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
