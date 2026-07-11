---
title: Full Test-Coverage Plan — Tools, Frameworks & Phases (all 1,941 TCs)
created: 2026-06-23
status: proposed
supersedes_partial: TEST-AUTOMATION-PLAN-2026-06-19.md (E2E-only; folded in as the FE-E2E track)
stack: Angular 20 + ASP.NET Core 10 + PostgreSQL
goal: bind every designed IEEE-829 TC to an automated runner by test-type, raise execution from ~0% toward full coverage
---

# Full Test-Coverage Plan

Extends the 2026-06-19 E2E plan into a **complete tool stack** covering all **1,941 designed TCs** across the
8 test-type categories. The 2026-06-19 plan (Playwright E2E + correlation-ID log gate) becomes the
**FE-E2E track** inside Phase 2–4 here.

> **Honest framing first.** "Cover all 1,941" should mean *every TC is bound to a runner and has a tracked
> verdict* — NOT "automate 100%." ~14 are `status: blocked`/deferred (no system to test); a slice of
> exploratory/visual TCs are cheaper as manual. Target: **~90% automated, ~10% manual-tracked**, all with a
> recorded status. Chasing literal 100% automation is negative-ROI.

## 1. Tool → test-type mapping (how each of the 8 categories gets covered)

| Test type | TCs | Primary tool(s) | Layer |
|---|---:|---|---|
| Happy path | 601 | **xUnit + Testcontainers** (BE integration) · **Playwright** (FE E2E) · Karma/Jasmine (component) | API + UI |
| Negative | 940 | xUnit (`[Theory]` invalid inputs) · Karma (form/validation) · Playwright (error flows) | API + UI |
| Boundary | 507 | xUnit **`[Theory]`/`[InlineData]`** data-driven · Karma boundary specs | API + UI |
| Security | 800 | xUnit authz/permission tests · **role-string contract test** · **OWASP ZAP** (DAST) · `/security-audit` skill | API + DAST |
| Multi-tenant isolation | 392 | **Dedicated xUnit integration suite** (Testcontainers, 2 tenants) · Playwright cross-tenant | API + UI |
| Performance | 144 | **k6** (API load/SLA) · Playwright/**Lighthouse** (page timings) | API + UI |
| Accessibility | 91 | **@axe-core/playwright** · Angular CDK a11y | UI |
| Cross-browser | 105 | **Playwright projects** (chromium/firefox/webkit) | UI |

**Cross-cutting (not a TC type, but the spine):**
| Concern | Tool | Why it's here |
|---|---|---|
| **FE↔BE contract** | **OpenAPI schema-diff gate** (BE Swagger JSON ⇄ FE models) — or Pact | Your #1 recurring leak (`/tenant/` prefix, `ApiResponse<T>` envelope, RPT-001 shape drift). Fails build on divergence. |
| Architecture rules | **NetArchTest** | Enforces Api→Application→Domain boundaries as tests |
| Test quality | **Stryker.NET + StrykerJS** (mutation) | Proves the 2,333 + 3,624 existing tests actually assert |
| Observability gate | Correlation-ID + Serilog JSON (from 2026-06-19 plan) | Fail any test whose request logged ERROR/WARN |
| TC traceability | Custom reporter — `@TC-XXX` tag → flips TC `status:` | Turns "0% executed" into a live rising metric |
| CI | GitHub Actions (compose up → run → artifacts) | Gates every PR |

## 2. Traceability mechanism (the thing that makes "cover all" measurable)
- Every automated test is tagged with the TC id(s) it covers: `[Trait("TC","TC-CHR-001")]` (xUnit) /
  `test('@TC-ADM-001 …')` (Playwright) / `it('@TC-AUTH-003 …')` (Karma).
- A reporter maps results back and drives each `docs/QA/**/TC-*.md` `status:` `draft → automated → passed/failed`.
- **Coverage = % of 1,941 TCs with a non-draft status.** Today ≈ 0%. This number is the plan's KPI.

## 3. Phased roadmap

### Phase 0 — Foundation (1 week) · *no feature coverage yet*
- Stand up the harnesses: `e2e/` Playwright project, `HRM.Tests` integration profile (already exists — extend), `k6/`, `contract/` (schema-diff).
- Backend: `X-Correlation-Id` middleware + Serilog compact-JSON sink (test profile); `/health` compose healthcheck; **idempotent test seed** (2 tenants + 5 personas + baseline data).
- **Contract gate v1**: CI job that diffs live Swagger JSON against generated FE TS models — fails on drift.
- TC-status reporter + `@TC` tagging convention. GitHub Actions skeleton.
- **Exit:** one green test end-to-end through CI (stack up → login → assert → artifacts → teardown) + contract gate live.

### Phase 1 — Contract + Smoke + Regression (1 week) · *highest ROI, catches today's bug classes*
- **Contract suite**: assert every FE service base URL + DTO shape matches its BE controller route + response (kills the `/tenant/` prefix + envelope + role-string bug classes).
- **Persona route-access matrix** (Playwright): each persona × every route = renders or *deliberate* 403. Catches BUG-1/2/3.
- **Regression pack** pinning BUG-1..10.
- **Exit:** smoke + contract green in CI; the FE↔BE drift class can no longer ship green.

### Phase 2 — Functional coverage: Happy + Negative + Boundary (4–6 weeks) · *the bulk: ~2,048 TCs*
Module-by-module, in project priority order (Auth → Core HR → Leave → Attendance → Recruitment → Payroll → Performance → Admin Console → Onboarding → Reports → Notifications):
- **BE**: xUnit + Testcontainers integration per endpoint — happy (601) + negative (940); boundary (507) as `[Theory]`/`[InlineData]`.
- **FE**: Karma component specs (validation/error states) + Playwright E2E for critical journeys.
- One **agent per module**, non-overlapping paths, parallelizable.
- **Exit:** ≥90% of Happy/Negative/Boundary TCs bound + passing per module.

### Phase 3 — Security + Tenant Isolation (2–3 weeks) · *1,192 TCs, the platform's #1 rule*
- **Tenant-isolation suite** (392): xUnit integration with two seeded tenants — persona in tenant A can never read/write tenant B, asserted at API *and* UI. Its own CI gate.
- **Security (800)**: authz/permission matrix tests; **OWASP ZAP** baseline DAST scan against the running app in CI; wire the existing `/security-audit` skill into the PR gate.
- **Exit:** isolation suite green (0 breaches) + ZAP baseline clean + authz TCs bound.

### Phase 4 — Non-functional: Performance + a11y + Cross-browser (2 weeks) · *340 TCs, your biggest design gap*
- **Performance (144)**: k6 scripts over JWT-authed API flows with SLA thresholds (p95 latency, error rate); Lighthouse/Playwright page-timing budgets.
- **Accessibility (91)**: `@axe-core/playwright` WCAG checks on every key page (drops into existing Playwright run).
- **Cross-browser (105)**: enable Playwright firefox + webkit projects on the smoke + critical suites.
- **Exit:** k6 thresholds enforced in CI; axe a11y gate (0 serious violations); critical journeys green on 3 browsers.

### Phase 5 — Hardening & quality gates (1 week, then ongoing)
- **Mutation testing**: Stryker.NET (BE) + StrykerJS (FE) on core domains — set a survived-mutant threshold.
- **NetArchTest** architecture rules in CI.
- **Coverage gates**: Coverlet (BE) + karma/jest coverage → fail PRs under threshold.
- **Karma→Jest migration** (Karma is deprecated) — schedule, not urgent.
- **Exit:** all gates enforced on every PR; coverage KPI dashboard published.

## 4. Coverage math (how all 1,941 land)
| Phase | TC types covered | TCs | Cumulative |
|---|---|---:|---:|
| 1 | contract/regression (cross-cutting) | — | smoke |
| 2 | Happy + Negative + Boundary | 2,048* | ~bulk |
| 3 | Security + Isolation | 1,192* | ~all functional+security |
| 4 | Performance + a11y + Cross-browser | 340 | **all 8 types** |
| 5 | quality of the above (mutation/coverage) | — | hardened |

\*Type tallies overlap (a TC can carry multiple tags); union = 1,941. Deferred (~14) stay manual-tracked.

## 5. Effort & sequencing
- **~11–14 weeks** to full coverage with 1 dev; **~4–6 weeks** with module-parallel agents (Phase 2–3 fan out).
- Front-load **Phase 0–1** (2 weeks) — they pay for themselves immediately by stopping the contract-drift bug class.
- Phases 2–4 are per-module parallelizable; Phase 5 is continuous.

## 6. New dependencies to add
- BE: `Stryker.NET`, `NetArchTest.Rules`, (optional `Respawn`, `Bogus`) — `HRM.Tests` already has xUnit + Testcontainers + FluentAssertions.
- FE: `@axe-core/playwright`, `@stryker-mutator/*`; enable firefox/webkit in `playwright.config.ts`.
- Repo: `k6` (binary/CI action), an OpenAPI schema-diff tool (or `Pact`), **OWASP ZAP** CI action.
- CI: GitHub Actions jobs for compose-up, playwright, k6, ZAP, contract-diff, mutation (nightly).

## 7. My recommendation on scope
Do **Phase 0 + 1 now** (2 weeks) — that alone kills your two worst patterns (FE↔BE drift, cross-layer auth bugs) and makes the 0%→tracked switch. Then run Phase 2–4 **module-by-module behind `/implement-all`-style agents** rather than as one big-bang. Skip literal 100% automation; track the deferred/exploratory slice manually.
