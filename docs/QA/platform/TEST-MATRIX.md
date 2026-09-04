# Platform Module — Test Matrix

> Cross-cutting platform / defense-in-depth stories (US-PLT). Naming is **trait-driven** (the doc id matches
> the `[Trait("TC", …)]` string in code so the runner binding survives) with a running numeric suffix for
> net-new stories that have no trait yet. Existing US-PLT-002 / US-PLT-005 TCs are **automated regression TCs**
> binding already-green xUnit arms; US-PLT-006 (error tracking / GlitchTip) is **net-new and 0% built**, so its
> TCs are **forward-looking `draft` specs** that flip to `automated` once the SDK layer and its `[Trait]` arms
> land. Numbers continue past the highest used trait (…006/007 taken by US-PLT-005) → US-PLT-006 uses
> TC-PLT-008…014 + TC-PLT-ISO-001, with no id collision.

## Story → Test Case Coverage

| User Story | Title | Priority | Test Cases | Status |
|-----------|-------|----------|-----------|--------|
| US-PLT-002 | PostgreSQL RLS as defense-in-depth tenant isolation | Should Have | TC-PLT-002-RLS | automated |
| US-PLT-004 | Observability & platform NFRs (OTel, health, per-tenant usage, SLOs) | Should Have | **(none authored)** — no TC doc in this directory binds `user_story: US-PLT-004` | **unbound** — green xUnit arms exist but carry no `[Trait("TC", …)]`, so nothing is traceable |
| US-PLT-005 | Encryption-at-rest for sensitive PII (KEK/rotation) | Must Have | TC-PLT-P34, TC-PLT-003, TC-PLT-004, TC-PLT-006, TC-PLT-007 | automated |
| US-PLT-006 | Error tracking via self-hosted GlitchTip | Should Have | TC-PLT-008, TC-PLT-009, TC-PLT-010, TC-PLT-011, TC-PLT-012, TC-PLT-013, TC-PLT-014, TC-PLT-ISO-001 | draft (007-006 net-new, 0% built) |

## US-PLT-004 — AC → TC Coverage

> **Added 2026-09-04 (F4 / GAP-030).** US-PLT-004 shipped 2026-07-30 (+ the per-tenant API-call counter
> 2026-07-31, commit `b9906626`) but has **never had a TC doc**. This section records the coverage hole
> honestly rather than leaving the story absent from the matrix. **No row here may be marked `pass`.**
>
> ⚠ **`TC-PLT-004.md` is NOT this story's test case.** Its frontmatter binds `user_story: US-PLT-005`
> (bulk re-encrypt sweep). The filename collision is an artefact of the trait-driven naming scheme —
> **read frontmatter, never filenames.** The same applies to TC-PLT-003/006/007.

| AC | Requirement | Test Case(s) | Backing arms in code (untraited) | Status |
|----|-------------|--------------|----------------------------------|--------|
| AC-1 | OTel traces + metrics emitted to a configured exporter/store | *(none)* | `ObservabilityExtensionsTests` covers the **producer** side only | **UNMET — producer only.** No collector/Tempo/Prometheus/Grafana in any compose file or `ops/`; there is no store for the exporter to reach. |
| AC-2 | `/health/live` + `/health/ready` report liveness/readiness accurately | *(none)* | `PlatformMonitoringRedisHealthTests` | **met, untraced** — Postgres hard-fails; Redis reports **Degraded, not Unhealthy**; no Hangfire check. |
| AC-3 | Monitoring shows real error-rate %, P95 latency, SLA/uptime (no hard-coded nulls) | *(none)* | `PlatformMonitoringIntegrationTests`, `PlatformMonitoringUsageGaugesPostgresTests` | **PARTIAL.** BE computes real values; the **FE computes `latencyTrend24h`/`topErrors` and then discards them** (never rendered) and `metricsStatus` is hardcoded. |
| AC-4 | Per-tenant counters (API calls, storage, emails) recorded + exposed | *(none)* | `TenantApiCallUsagePostgresTests`, `PlatformMonitoringUsageGaugesPostgresTests` | **met, untraced** — API-call counter shipped `b9906626` (table + migration + dormant RLS policy + hot-path write + live gauge). |
| AC-5 | An SLO (e.g. login p95) is instrumented and measurable | *(none)* | — | **UNMET.** No login-latency SLI: `LoginCommandHandler` records an **outcome counter only**, and `/api/v1/auth` sits on the shared allow-list, so login is **excluded** from `tenant_latency_bucket`. |
| Multi-tenant | Usage/latency counters and monitoring aggregates are tenant-scoped | *(none)* | `TenantApiCallUsagePostgresTests` (tenant-scoped upsert), EF global query filters | **met, untraced** |

**Coverage verdict: 0/5 AC test-bound.** AC-2/AC-4 and the isolation guarantee have green arms that
carry no `[Trait("TC", …)]`, so they bind to no TC id. AC-1, AC-3 and AC-5 are **genuinely unmet in
product code** — authoring TCs for them would produce specs that cannot pass, so they are recorded as
unmet here and in [`US-PLT-004`](../../BA/platform/US-PLT-004.md) §4-§10 rather than papered over.

## US-PLT-006 — AC → TC Coverage

| AC | Requirement | Test Case(s) | Type | Status |
|----|-------------|--------------|------|--------|
| AC-1 | Exception captured with stack trace + release/version, tagged `tenant_id`/`tenant_subdomain` | TC-PLT-008 | functional | draft |
| AC-2 | `BeforeSend` scrubs request body / `Authorization` / cookies/session / email / national ID; `SendDefaultPii=false`; PII must NOT leave | TC-PLT-009 | security | draft |
| AC-3 | Blank DSN ⇒ SDK inert (no init/network), app unaffected; DSN via user-secrets/env only, never committed | TC-PLT-010 | security | draft |
| AC-4 | Telemetry in-boundary (self-hosted); additive to Serilog console+file (file = RequestId QA log); no cloud egress | TC-PLT-011 | integration | draft |
| AC-5 | Serilog Sentry sink at Error level + ASP.NET Core `UseSentry`; OTel wiring untouched | TC-PLT-012 | integration | draft |
| AC-6 *(optional/phase-2)* | Angular `@sentry/angular` client capture with mirrored scrub + subdomain tenant tag | TC-PLT-013 | e2e | blocked (phase-2) |
| AC-7 | GlitchTip Postgres volume (`gt-pgdata`) included in the backup routine | TC-PLT-014 | functional | draft |
| Multi-tenant | Two tenants' errors tenant-tagged, never cross-attributed | TC-PLT-ISO-001 | security / isolation | draft |

All 7 ACs covered (AC-6 as a deliberately deferred phase-2 slice) + the mandatory multi-tenant isolation TC.

## Multi-Tenant Isolation (mandatory per module)

| TC | Guarantee |
|----|-----------|
| TC-PLT-ISO-001 | Tenant A's captured error carries only A's tags; tenant B's only B's; no cross-attribution even under interleaved requests (per-request scoped `ITenantContext`). |
| TC-PLT-002-RLS | (US-PLT-002) RLS enforces tenant isolation beneath the EF query filter on the `hrm_app` role — the DB-layer isolation this module's telemetry rides on. |

## Notes / Deviations

- **Trait-vs-counter:** US-PLT docs are normally trait-named (no running counter). US-PLT-006 is net-new with
  no code/trait yet, so the running numeric suffix continues from the highest used (007) → 008+; TC-PLT-006 and
  TC-PLT-007 are already bound to **US-PLT-005** (encryption), so they are NOT reused here.
- **Status honesty:** every US-PLT-006 TC is `draft` (or `blocked` for the phase-2 FE slice) because the
  Sentry/GlitchTip SDK layer is 0% wired (feasibility study, 2026-07-24). None may be marked `pass`/`automated`
  until a real binding arm runs. The crux security control is **TC-PLT-009** (PII scrub — the ADR's hard
  condition); it carries an explicit negative arm asserting a national-ID sentinel is ABSENT from the event.
