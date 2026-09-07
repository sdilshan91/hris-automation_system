---
id: US-PRF-012
module: Performance Management
priority: Could Have
persona: HR Officer / Manager
status: draft
blocked: true
blocked_by: "No isolation mechanism exists for a materialized view — see §2. NOT unblocked by US-PLT-002."
created: 2026-09-07
sprint: backlog
acceptance_criteria_count: 6
findings: ISSUE-129, BUG-003
---

# US-PRF-012: ⛔ BLOCKED — `performance_summary` Materialized View for Dashboard Aggregates

> # ⛔ DO NOT START THIS STORY
>
> **This story is BLOCKED, and it is not blocked on effort or on a schedule.** It is blocked because a
> materialized view is a physical **cross-tenant artifact** and this platform has **no isolation mechanism
> that can protect one**.
>
> **The block is CATEGORICAL, not temporal.** Do not read this as "wait for US-PLT-002 / RLS". PostgreSQL
> has **no row-level security for materialized views at all** — RLS applies to tables. Both the policy
> migration (`20260710120000_Platform_RlsPolicies_Dormant.cs:55`) and the enabler
> (`DbInitializer.cs:218`) filter on `table_type = 'BASE TABLE'`, so `performance_summary` would be skipped
> by construction. **Flipping `Rls:Enabled` buys this view nothing, ever.**
>
> ⚠ **A related correction:** RLS is **not** dormant on this platform. `appsettings.json:48-50` ships
> `"Rls": { "Enabled": true }`; only `appsettings.Development.json:14-16` overrides it to `false`. Any note
> claiming "RLS is dormant, so the view is parked until it's enabled" is wrong on both halves.
>
> **This story cannot simply be unblocked later. It requires a bespoke isolation mechanism to be designed
> and agreed first** (§3). Until that design exists and is accepted, there is nothing here to implement.

## 1. Description
**As an** HR Officer or Manager viewing the performance dashboard,
**I want** aggregate metrics to be served from a precomputed `performance_summary` materialized view
refreshed on a schedule,
**So that** the dashboard meets US-PRF-007 NFR-1 (2.5 s P95 at 5,000 employees) without recomputing every
aggregate on each request.

## 2. Why this is blocked — the isolation argument
Today the dashboard's aggregates are computed live with **tenant-scoped EF `GroupBy` queries**
(`IPerformanceDashboardService.cs:20-24`). Isolation is therefore automatic: the EF Core global query filter
applies to every query, and a developer cannot forget it.

A materialized view inverts that. It is **one physical relation holding every tenant's rows**. Consequences:

1. **The EF global query filter no longer protects the read.** Isolation degrades from a framework
   invariant to a **hand-written `WHERE tenant_id = @tenant` on every single read site**. One omission is a
   silent cross-tenant leak.
2. **RLS cannot substitute for it — ever.** See the banner. This is not a sequencing problem.
3. **This would be the first primary user-facing read in the codebase whose only tenant isolation is a
   hand-written predicate.** That is a new and worse isolation posture, introduced on a dashboard.
4. **The static analysis would not catch a missing predicate.** `.semgrep/tenant-isolation.yml` matches
   `ExecuteSqlRaw`, `ExecuteSqlRawAsync` and `FromSqlRaw` (lines 42-46). It does **not** match `FromSql`
   (interpolated) or `SqlQueryRaw` — the shapes a materialized-view read would actually use. So the guard
   rail that exists for raw SQL would be silently absent here.
5. **The blast radius is already demonstrated on this exact surface.** [[BUG-003]] is **CRIT and OPEN**, and
   is recorded as *"EXTENDED to the US-PRF-007 dashboard surface (cross-tenant READ + EXPORT leak for
   `.All` holders across overview/trend/drill-down/export)"* (`docs/QA/TEST-FINDINGS.md:1039`). The
   dashboard this story optimises **is currently leaking across tenants**. Adding a cross-tenant physical
   artifact to a surface with a live, unresolved cross-tenant leak inverts the correct order of work.

**Adding a matview here trades a documented latency concern (LOW) for an isolation regression on a surface
with an open CRIT.** That trade should not be made.

## 3. What would have to be true before this story can start
All four, and none of them is scheduled:

- **G-1 — A designed and accepted isolation mechanism for the view.** Candidates, none yet evaluated: a
  per-tenant security-barrier *view* wrapping the matview; a repository-level seam that makes the predicate
  structurally unforgettable; or partitioning per tenant. **This is the actual work item and it does not
  exist yet.**
- **G-2 — Detection extended to the read shapes involved.** `.semgrep/tenant-isolation.yml` must cover
  `FromSql`/`SqlQueryRaw` before any such read is written, or the platform loses its only mechanical guard.
- **G-3 — [[BUG-003]] resolved on the dashboard surface.** Optimising a leaking read before fixing the leak
  is the wrong order.
- **G-4 — A measured latency case that the cache does not already satisfy.** See §4.

## 4. The cheap half is not shipped either — and it captures most of the benefit
[[ISSUE-129]] bundles three things: a Redis read-through cache, the `performance_summary` matview, and a
4-hourly Hangfire refresh. **The Redis cache half is the one worth doing, and it is _not_ merged.**

⚠ **Correction:** PR **#688 is not in the merged history** of `test/local-subdomains` (verified against
`git log`; #679, #686 and #691 are present, #688 is not). Any note stating the cache "already shipped" is
wrong. What is true is that the seam is ready: Redis is wired, fail-open, with an established tenant-scoped
key convention (`t:{tenantId}:`), and `HrReportService` already performs read-through keyed by
`(tenantId, filter-hash)`.

The cache is **ordinary, unblocked work with no isolation implications** (its keys are tenant-prefixed, so
it does not create a cross-tenant artifact) and it is expected to capture the large majority of the latency
win. **Ship the cache; measure; then re-evaluate whether this story has a remaining problem to solve.**
[[ISSUE-129]] is filed **LOW**, and its own severity rationale notes the gap "matters only at the
5,000-employee scale".

## 5. Acceptance Criteria (IEEE 830 §3.2) — PROVISIONAL, NOT BUILDABLE
> Recorded so the story is complete, **not** as a licence to implement. AC-1 is unsatisfiable until G-1 in §3
> is designed, and no other AC may be attempted before it.

| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | **The agreed isolation mechanism from G-1 exists** | Any read of `performance_summary` is executed | The read is provably tenant-scoped by that mechanism — **not** by a hand-written predicate a developer must remember, and the property is enforced by a test that fails when the predicate is removed |
| AC-2 | The view holds rows for Tenant A and Tenant B | A Tenant A caller loads the dashboard | Only Tenant A's aggregates are returned; a mutation test that deletes the tenant predicate from the read **fails the suite** (the guard is real, not incidental) |
| AC-3 | Review data changes | The scheduled refresh runs | `performance_summary` is refreshed on the tenant-configurable interval (US-PRF-007 BR-4, default 4 h) and the dashboard reflects the refreshed data |
| AC-4 | The view is stale between refreshes | A caller loads the dashboard | The staleness is bounded, disclosed to the caller, and consistent with BR-4 — an aggregate silently up to 4 hours old is a correctness statement the UI must make |
| AC-5 | A tenant has 5,000 employees | The dashboard is loaded | P95 load time is under 2.5 s (US-PRF-007 NFR-1), **measured against the cache-only baseline from §4** so the view's marginal benefit is demonstrated rather than assumed |
| AC-6 | The refresh job runs | It executes for all tenants | It carries explicit tenant context per the background-job convention, and a refresh failure for one tenant does not abort the others or serve another tenant's stale rows |

## 6. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1: **Tenant isolation (overriding).** No read of `performance_summary` may rely solely on a hand-written predicate. If the design cannot guarantee isolation structurally, **the view must not be built** — the correct outcome is to close [[ISSUE-129]]'s view half as WONTFIX and keep the live queries.
- NFR-2: The matview must not become a second source of truth that can silently disagree with the live aggregates; a reconciliation test must compare view output against the live `GroupBy` result.
- NFR-3: The DTO contract (`IPerformanceDashboardService`) must not change — it is the documented stable seam.
- NFR-4: Refresh must not lock the dashboard read path (`REFRESH MATERIALIZED VIEW CONCURRENTLY` or equivalent).

## 7. Business Rules
- BR-1: The view is a **performance optimisation only**; it must never change a reported figure. Any divergence from the live calculation is a defect in the view.
- BR-2: Refresh interval is tenant-configurable, default 4 hours (US-PRF-007 BR-4).
- BR-3: Scope rules are unchanged — `.View.All` sees org-wide, `.View.Team` sees direct reports only. The view must not become a route around the scope resolution.
- BR-4: If isolation cannot be guaranteed structurally, live queries are retained. **Correctness of isolation outranks the latency NFR.**

## 8. Data Requirements
- **Storage (proposed):** `performance_summary` materialized view aggregating review data by `tenant_id`, department, grade and cycle (US-PRF-007 §7).
- `tenant_id` must be part of every unique/refresh key and of every read predicate.
- **No schema design is committed here** — it depends entirely on the G-1 isolation mechanism.

## 9. Dependencies
- **US-PRF-007** — owns the dashboard, NFR-1, NFR-3 and BR-4; this story exists to satisfy its NFR-3.
- **[[ISSUE-129]]** — the originating finding (LOW, OPEN). Its cache half (§4) is separate, unblocked, and should be done first.
- **[[BUG-003]]** — CRIT, OPEN, confirmed leaking on this dashboard. **G-3.**
- **US-PLT-002** — referenced only to record that it is **NOT** the unblocker. Do not link this story to its completion.

## 10. Open Questions — all unresolved; this story is not `ready`
- **OQ-1.** What is the isolation mechanism (G-1)? Security-barrier view, repository seam, per-tenant partitioning, or something else? **No option has been evaluated.** This is a platform-architecture decision requiring a human and probably an ADR.
- **OQ-2.** Should this be built at all? *Recommendation (confidence 80%):* **no — ship the Redis cache, measure, and close the view half as WONTFIX unless a measured 5,000-employee case still misses NFR-1.* The finding is LOW; the risk is an isolation regression on a surface with an open CRIT.
- **OQ-3.** If built, is the staleness window (AC-4) acceptable to the product for a dashboard HR uses for calibration decisions?

## 11. Assumptions & Constraints
- PostgreSQL provides **no RLS for materialized views**. This is a database constraint, not a project decision, and no amount of platform work changes it.
- The `IPerformanceDashboardService` docstring pre-authorises this design as a "future story" — that predates the isolation analysis above and should **not** be read as an approval to proceed.
- Live aggregates are currently **exact and tenant-isolated**; the status quo is correct, merely slower.

## 12. Test Hints
> Applicable only if OQ-1 and OQ-2 are resolved in favour of building.
- **The test that matters most:** delete the tenant predicate from a view read and assert the suite goes **red**. If it stays green, the isolation is not real and the view must not ship. (Per the "verify the verifier" principle — a green isolation test is not evidence until it has been watched to fail.)
- Cross-tenant read: seed Tenant A and Tenant B aggregates, refresh, and assert a Tenant A caller sees zero Tenant B contribution in every widget — overview, trend, drill-down **and export** (all four are the surfaces BUG-003 leaks on).
- Reconciliation: compare every view-served figure against the live `GroupBy` for the same fixture; any difference fails.
- Refresh: assert reads are not blocked during refresh, and that a per-tenant refresh failure neither aborts other tenants nor serves stale cross-tenant rows.
- Baseline: measure the 5,000-employee P95 with the Redis cache **only**, then with the view, and record both — AC-5 requires the marginal benefit to be demonstrated.
