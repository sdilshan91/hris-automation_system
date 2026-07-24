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
| US-PLT-005 | Encryption-at-rest for sensitive PII (KEK/rotation) | Must Have | TC-PLT-P34, TC-PLT-003, TC-PLT-004, TC-PLT-006, TC-PLT-007 | automated |
| US-PLT-006 | Error tracking via self-hosted GlitchTip | Should Have | TC-PLT-008, TC-PLT-009, TC-PLT-010, TC-PLT-011, TC-PLT-012, TC-PLT-013, TC-PLT-014, TC-PLT-ISO-001 | draft (007-006 net-new, 0% built) |

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
