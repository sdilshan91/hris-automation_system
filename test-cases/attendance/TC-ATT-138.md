---
id: TC-ATT-138
user_story: US-ATT-010
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-15
---

# TC-ATT-138: Dashboard KPI Redis cache + DB fallback -- cache refreshed on clock-in/out; DB-computed path verified now (Redis CONDITIONAL/DEFERRED)

## 1. Test Objective
Verify the dashboard-KPI caching contract (FR-7, NFR-1): the dashboard KPIs are served from a tenant-scoped Redis cache that is refreshed on each clock-in/out event, with a database-computed fallback when Redis is unavailable. Redis is NOT assumed wired; this TC verifies the DB-computed KPI path live and records the cache-specific assertions (key shape, refresh-on-event, cache-hit latency) as CONDITIONAL/DEFERRED on the Redis layer.

## 2. Related Requirements
- User Story: US-ATT-010
- Functional Requirements: FR-7 (dashboard KPIs cached in Redis, refreshed on each clock-in/out)
- Non-Functional: NFR-1 (dashboard < 2s P95, leveraging Redis-cached KPIs)
- Assumptions: §10 (if Redis is unavailable, fall back to a database query with degraded performance)
- Data: §7 cache keys att_dashboard:{tenant_id}:{date}:{metric}
- API: GET /api/v1/attendance/dashboard

## 3. Preconditions
- Tenant "acme"; HR Officer authenticated.
- Seeded workforce state as in TC-ATT-129 (expected 17, clocked-in 12).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| cache key pattern | att_dashboard:{tenant_id}:{date}:{metric} | §7 |
| metrics | expected / clocked_in / on_leave / absent / attendance_pct | §7 |
| date | 2026-06-15 | today |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /dashboard` with Redis UNAVAILABLE (or not wired) | 200 OK; KPIs computed from the database -- the dashboard still loads (degraded performance acceptable per §10), values correct (reconcile to TC-ATT-129). |
| 2 | Verify the fallback is silent | no 500 / no empty dashboard when the cache is absent -- the DB path is the safety net. |
| 3 | (CONDITIONAL on Redis) cache key shape | KPI cache keys are tenant-scoped per §7 (`att_dashboard:{tenant_id}:{date}:{metric}`) so two tenants never collide -- design verified now, asserted live once Redis lands (reuses TC-ATT-ISO-004 / TC-ATT-ISO-013 for key isolation). |
| 4 | (CONDITIONAL on Redis) refresh-on-event | a clock-in/out for the date refreshes the affected KPI cache entries so the next dashboard read reflects the new count -- the DB-recompute equivalent is verified now (TC-ATT-129 Step 8). |
| 5 | (CONDITIONAL on Redis) cache-hit latency | a warm cache read meets the NFR-1 < 2s P95 target; the DB-computed path latency is measured in TC-ATT-139. |
| 6 | Stale-cache guard | if a cache entry is stale/missing for one metric, the dashboard recomputes/falls back rather than serving a wrong KPI. |

## 6. Postconditions
- The dashboard serves correct KPIs via the DB-computed path with Redis absent; the cache contract is documented for when Redis lands.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Performance test
- [ ] Multi-tenant isolation
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- **Redis KPI cache (FR-7 / NFR-1) CONDITIONAL/DEFERRED:** the Redis layer is not assumed wired; consistent with the module-wide deferred-Redis handling (US-ATT-001 FR-6 TC-ATT-001, US-ATT-002 FR-5, US-ATT-007 FR-8 TC-ATT-098). The DB-computed KPI path + the tenant-scoped key design are verified now; the refresh-on-event, cache-hit SLA, and TTL are asserted once the cache exists. **Reported to caller.**
- The cache-key tenant isolation is realised in TC-ATT-ISO-013 (reusing TC-ATT-ISO-004); the warm-vs-cold latency is measured in TC-ATT-139.
