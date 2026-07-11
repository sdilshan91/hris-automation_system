---
id: TC-RPT-ISO-020
user_story: US-RPT-005
module: Reports & Analytics
priority: high
type: security
status: fail
exec_note: "2026-06-30 API iso-fixture probe: report cache key NOT tenant-isolated — isoa-tok+isob-hdr headcount returned isob's cached result (generatedAt reused from isob own call). Cross-tenant cache collision. BUG-003 class (ISSUE-193)."
created: 2026-06-17
---

# TC-RPT-ISO-020: Dashboard cache keys are tenant + role + user scoped; no cross-tenant or cross-user cache collision (AC-5, FR-4/8) -- Redis-conditional

## 1. Test Objective
Verify that the dashboard cache key `t:{tenantId}:dashboard:{role}:{userId}:{widgetKey}` includes tenant,
role, AND user, so that no two users -- across tenants or within a tenant -- can ever read each other's cached
widget data. Redis is DEFERRED dev-box infra: the cache-hit/key-shape steps are CONDITIONAL on Redis being
wired; if absent, assert the intended key derivation includes the tenant+role+user prefix and that responses
remain correctly isolated. Validates AC-5, FR-4, FR-8.

## 2. Related Requirements
- User Story: US-RPT-005
- Acceptance Criteria: AC-5
- Functional Requirements: FR-4 (Redis cache key `t:{tenantId}:dashboard:{role}:{userId}:{widgetKey}`), FR-8 (tenant scoping)
- Non-Functional: NFR-3 (tenant isolation)

## 3. Preconditions
- Tenant A users `hrA` and `empA`; Tenant B user `hrB`. Distinct dashboard data per user/tenant.
- (Redis-conditional) Redis wired and observable.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| key template | t:{tenantId}:dashboard:{role}:{userId}:{widgetKey} | FR-4 |
| hrA key prefix | t:{A}:dashboard:hr:{hrAId}: | per-user |
| hrB key prefix | t:{B}:dashboard:hr:{hrBId}: | different tenant |
| empA key prefix | t:{A}:dashboard:employee:{empAId}: | same tenant, different role+user |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `hrA`, load dashboard; (Redis-conditional) inspect written keys | keys prefixed `t:{A}:dashboard:hr:{hrAId}:{widgetKey}` (FR-4) |
| 2 | As `hrB`, load dashboard; inspect keys | keys prefixed `t:{B}:dashboard:hr:{hrBId}:...` -- distinct tenant segment; no collision with `hrA` |
| 3 | As `empA`, load dashboard; inspect keys | keys prefixed `t:{A}:dashboard:employee:{empAId}:...` -- same tenant as `hrA` but different role+userId segment; no collision |
| 4 | Force `hrA` then `hrB` to populate cache, then re-read each | each receives only its OWN cached widgets; no cross-tenant or cross-user cache hit |
| 5 | `GET ...?refresh=true` as `hrA` | only `hrA`'s keys are invalidated/refreshed; `hrB`/`empA` cache entries untouched (FR-4) |
| 6 | (DEFERRED/CONDITIONAL) If Redis is NOT wired | assert the intended key derivation includes tenant+role+user and that live responses are still isolated; step recorded PENDING for the cache-hit assertion |

## 6. Postconditions
- Cache keys are tenant+role+user scoped; no collision across tenants or users; refresh invalidates only the caller's keys.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
