# Tooling Adoption Plan — boosting the HRM SaaS agent workflow

> **Goal:** absorb the researched ADOPT + CONSIDER tools into our stack (Angular 20 + ASP.NET Core 10
> + PostgreSQL, multi-tenant, Claude-Code agent loop on Windows) to raise correctness, security, and
> velocity — **without** adding runtimes/services we don't need.
>
> **Organizing principle:** we run an *autonomous* agent loop, so the highest-value additions are
> **mechanical checks that give the loop a ground-truth correction signal** (a failed build, a killed
> mutant, a SAST hit) — not more prose. An analyzer that *fails the build* beats a skill that restates
> a rule. Waves are ordered by ROI × (low → high) risk/effort, with decision-gated bets last.

## Summary

| # | Tool | Wave | Effort | Integrates with | Gate |
|---|------|------|--------|-----------------|------|
| 0 | Confirm `postgres-mcp` read-only | 0 | 15 min | `.mcp.json` | — |
| 1 | Microsoft Learn MCP | 1 | 5 min | `.mcp.json`, all dev agents | — |
| 2 | ccusage | 1 | 5 min | loop telemetry | — |
| 3 | `@axe-core/playwright` (confirm binding) | 1 | ~½ hr | `@test-runner`, `/design-review` | — |
| 4 | Roslyn analyzers (Meziantou + SonarAnalyzer.CSharp) | 2 | ~2 hr | `Directory.Build.props`, `/implement-all` verify gate | — |
| 5 | angular-eslint `prefer-signals` | 2 | ~1 hr | `npm run lint`, verify gate | — |
| 6 | Gitleaks | 2 | ~2 hr | CI + `secret-guard` hook | — |
| 7 | Semgrep MCP + `p/csharp` baseline | 2 | ~½ day | `/security-audit`, `@test-runner` | — |
| 8 | Stryker.NET (nightly, `HRM.Application`) | 3 | ~½ day | scheduled job, `@test-authenticator` | — |
| 9 | Semgrep **custom tenant-isolation rules** | 3 | ~1 day | `/security-audit`, secret/tenant invariants | — |
| 10 | StrykerJS (FE `core/`) | 3 | ~½ day | scheduled job | — |
| 11 | **Self-hosted GlitchTip** + Sentry MCP | 4 | ~1 day | `@browser-debugger`, `@test-runner`, `/fault-diagnosis` | ✅ **DECIDED** (self-hosted, see ADR) |
| 12 | grafana/mcp-k6 | 4 | ~2 hr | `@test-runner`, `perf/` harness | AGPL/experimental |
| 13 | Dexter (pgdexter) index audit | 4 | ~½ day | periodic DB audit (Docker Postgres) | Ruby/HypoPG on Windows |
| 14 | Pact (pact-net) | 4 | ~2 wk | FE↔BE contract tests | **only if shape-drift recurs past Schemathesis** |
| 15 | Trivy | 4 | ~2 hr | CI, `/security-audit` | ✅ **ADOPT** (we're the hosting provider — supply-chain is ours) |
| 16 | NuGet MCP | 4 | 10 min | `@backend-dev`, package mgmt | dependency-drift pain |
| 17 | OpenSpec | 4 | ~½ day | upstream of `/implement-all` | **only if story quality is the bottleneck** |
| 18 | CodeRabbit | 4 | ~1 hr | PR review (external) | paid trial |
| — | Bookmarks: awesome-claude-code, PulseMCP | — | — | discovery | — |

---

## Wave 0 — Security check (do first, blocks nothing)

**0. Confirm `crystaldba/postgres-mcp` runs restricted/read-only.** It's our single most sensitive
MCP (live DB access). Verify the `.mcp.json` invocation pins its restricted mode, and confirm we are
**not** on the deprecated `@modelcontextprotocol/server-postgres` (archived for a `DROP SCHEMA`
SQL-injection bypass). *Verify:* attempt a write via the MCP in a scratch tenant → must be refused.

## Wave 1 — Trivial, zero-risk, immediate

**1. Microsoft Learn MCP** (official, hosted, read-only, zero secrets). Add to `.mcp.json`
(`https://learn.microsoft.com/api/mcp`, streamable HTTP). Grounds `@backend-dev` on .NET 10 / EF Core
/ ASP.NET Core / Hangfire-on-Postgres — the moving target agents hallucinate. Run *alongside*
context7 (broader/community). *Verify:* `/mcp` shows it connected; agent answers an EF-Core-10 API
question with a doc citation. *Owner:* all dev agents.

**2. ccusage** — `npx ccusage` (offline, reads local JSONL, cross-platform). Token/cost visibility for
unattended `/implement-all` + `/test-all` loops; informs the loop-budget guidance. *Verify:* prints a
daily/session report. *Owner:* orchestrator (loop cost awareness).

**3. `@axe-core/playwright`** — confirm `@test-runner`'s a11y checks use this binding (structured
violations), not ad-hoc axe. Directly serves our WCAG findings (BUG-096 contrast, BUG-238 tablist).
*Verify:* an a11y TC emits structured violations. *Owner:* `@test-runner`, `/design-review`.

## Wave 2 — Mechanical enforcement (the compile/CI correction signal — highest ROI)

**4. Roslyn analyzers — Meziantou.Analyzer + SonarAnalyzer.CSharp.** Add both via
`Directory.Build.props` (hits all four projects) with a **tuned ruleset** (start conservative to avoid
drowning the agent). Catches async/culture/DI + injection bugs LLM-written C# produces; a failed build
makes `@backend-dev` self-correct inside the remediation loop. *Integrate:* the analyzers run as part
of `dotnet build`, which is already the first step of the `/implement-all` verify gate — so this
strengthens an existing gate for free. *Risk:* noise → curate the ruleset in the first PR; treat
warnings-as-errors only for a hardened subset. *Verify:* introduce a known async bug → build fails.

**5. angular-eslint `prefer-signals`** (+ `inject()`-over-constructor-DI rules). Keeps agent-written FE
on our signals/standalone rails. Wire into `npm run lint` (already in the verify gate). *Verify:* an
`@Input()` that should be `input()` is flagged. *Owner:* `@frontend-dev`.

**6. Gitleaks.** Secret scanner over **history** (our `secret-guard` hook only sees *pending* writes).
Run as a pre-commit + a CI job; add a `.gitleaks.toml` allowlisting the known-safe example files
(`.env.example`, appsettings templates). Belt-and-suspenders for a PII platform. *Verify:* a planted
fake key in history is caught. *Owner:* CI + `secret-guard` (complementary).

**7. Semgrep MCP + `p/csharp` baseline.** Official Semgrep MCP (stdio, reads code only, no DB/secret
access). Gives `/security-audit` real SAST over C# **and** TypeScript instead of pattern-grep. Start
with the maintained registry pack; tune out noise. *Verify:* Semgrep flags a deliberate injection.
*Owner:* `/security-audit`, `@test-runner`.

## Wave 3 — QA hardening (needs tuning/time; scheduled, not per-PR)

**8. Stryker.NET** — mutation testing, **nightly** on `HRM.Application` (the CQRS handlers). Mechanically
proves whether a test would catch a regression — kills the "test theater" `@test-authenticator` hunts
by eye. Too slow for per-PR; run as a scheduled job and feed the mutation report to
`@test-authenticator`. *Verify:* a test with no real assertion shows a surviving mutant. *Owner:*
`@test-authenticator`.

**9. Semgrep custom tenant-isolation rules.** Where Semgrep beats generic analyzers *for our threat
model*: author rules encoding the BUG-003 class — e.g. flag `IgnoreQueryFilters()` outside the tenant
resolution middleware; flag a `DbSet` query on a `BaseEntity` with no tenant predicate; flag raw SQL
without a `tenant_id` clause. This turns our #1 systemic risk into a mechanical gate. *Verify:* a query
that drops the tenant filter is flagged red. *Owner:* `/security-audit`.

**10. StrykerJS** — mutation testing scoped to FE `core/` (auth, tenant, interceptors) where a masked
bug is most dangerous. Scheduled (Karma mutation runs are slow). *Owner:* `@test-authenticator`.

## Wave 4 — Decision-gated bets (planned, but each has an explicit gate)

> **Governance update (2026-07-08) — HRM ships as a hosted SaaS.** We host + are liable for customer
> HR PII, so several gates now resolve. See [ADR — SaaS data-governance posture](vault/decisions/ADR-2026-07-08-saas-data-governance-posture.md):
> - **Sentry (#11): DECIDED → self-hosted GlitchTip** (Sentry-compatible) + SDK PII scrubbing — no
>   third-party ingestion of PII-bearing exceptions.
> - **Trivy (#15): ADOPT** — as the hosting provider the supply-chain risk is ours.
> - **NEW — Postgres RLS (defense-in-depth tenant isolation): PLANNED.** Was in SKIP ("only with a
>   compliance driver"); that driver now exists. App-layer isolation (query filters +
>   `TenantAccessGuardMiddleware`) stays; RLS is an *additive* second layer in the DB. Weeks of work —
>   sequence deliberately. **The most important consequence of going SaaS.**
> - **Custom Semgrep tenant rules (#9): PRIORITY** · **Gitleaks → hard gate** after the historical
>   secret is rotated/purged · **encryption-at-rest (US-PLT-005)** + **full audit logging (BUG-082)**
>   become compliance-required.
> - CodeRabbit (#18) skip-for-now · Pact (#14) wait · OpenSpec (#17) skip · Dexter (#13) defer (also
>   validates RLS index leading-column).


**11. Sentry + Sentry MCP.** Best-in-class error tracking for a multi-tenant SaaS; the MCP lets
`@browser-debugger` / `@test-runner` pull the *exact* exception + release behind a failing TC instead of
grepping Serilog files. **GATE — this is a product + data-governance decision, not just an MCP:** Sentry
ingests exception data that on an HRM/PII platform can contain personal data, so evaluate **self-hosted
Sentry / GlitchTip** vs SaaS, and configure PII scrubbing before adopting. Alternative path: ship
Serilog → **Loki**, then [grafana/mcp-grafana]. *Decide first; the MCP is free once you do.*

**12. grafana/mcp-k6** — lets `@test-runner` author/validate k6 scripts conversationally (we have a
`perf/` k6 harness). **GATE:** experimental + AGPL-3.0 — confirm the license is acceptable. Prefer over
community k6-MCP forks.

**13. Dexter (pgdexter)** — automatic Postgres index advisor; catches the *`tenant_id`-must-lead-the-
index* perf trap that hurts RLS/tenant queries. Run as a **periodic human audit against the Dockerized
Postgres** (Ruby + HypoPG extension is fiddly on native Windows PG18), not an agent loop. Feeds an ADR
if it finds a missing composite index.

**14. Pact (pact-net)** — consumer-driven contract tests for our documented FE↔BE shape-drift bug class
(BUG-099/127/236, envelope/URL-prefix mismatches). **GATE:** ~2 weeks of setup (broker + both sides
instrumented), and our Schemathesis harness already catches ~80% read-only. **Adopt only if shape-drift
keeps recurring** after the current harness. Otherwise stay with Schemathesis.

**15. Trivy** — SCA (dependency CVEs) + container image scanning, single Go binary (clean Windows/agent
fit). **GATE:** adopt when/if we ship Docker images (our local Docker stack exists but isn't the
deploy artifact yet). Pairs with `dotnet list package --vulnerable` in CI. Skip OWASP Dependency-Check
(Java-oriented).

**16. NuGet MCP** (official) — real-time package metadata + guided updates (versions, CVEs, transitive
deps). **GATE:** adopt if dependency drift/CVEs start biting; `dotnet list package --vulnerable` in CI
may already suffice. *Owner:* `@backend-dev`.

**17. OpenSpec** — lightweight per-change spec folders (proposal/specs/tasks, ~250-line cap) upstream of
`/implement-all`. **GATE:** we already have IEEE-830 stories + STATUS.md, so **only if story quality is
the actual bottleneck.** Likely SKIP; kept here for completeness because it's the best-fit of the
spec-kit family.

**18. CodeRabbit** — external AI PR-review as an independent second opinion on stacked PRs, complements
`test-authenticator`/`integration-enforcer`/`/security-audit`. **GATE:** paid — trial on one repo before
committing.

**Bookmarks (not tools):** [hesreallyhim/awesome-claude-code], [PulseMCP], [official MCP registry] —
the highest-signal discovery surfaces for future scouting. Skip the auto-scraped mega-lists.

---

## How this wires into the existing agent system

- **`/implement-all` verify gate gets sharper** — the Roslyn analyzers (Wave 2) run inside the existing
  `dotnet build`; angular-eslint inside `npm run lint`. No new gate step; the existing ones just catch
  more, and the remediation loop self-corrects against real diagnostics.
- **`/security-audit` gains teeth** — Semgrep (Wave 2) + custom tenant rules (Wave 3) replace
  pattern-grep with AST-level SAST that encodes our own invariants; Gitleaks + Trivy cover secrets +
  deps. This is the biggest upgrade to a report-only skill in the suite.
- **`@test-authenticator` gains ground truth** — Stryker/StrykerJS (Wave 3) turn "this test looks fake"
  into "this mutant survived," a mechanical verdict.
- **Root-cause loop gets faster** — Sentry MCP (Wave 4) feeds `@browser-debugger` / `/fault-diagnosis`
  the exact exception behind a failing TC, replacing Serilog file-grep once logs are centralized.
- **Loop economics become visible** — ccusage (Wave 1) quantifies what the unattended loops cost.

## Cross-cutting risks & guardrails

- **Analyzer/SAST noise** is the top adoption risk — every Wave-2/3 tool ships with a *tuned* ruleset
  in its first PR; warnings-as-errors only for a hardened subset, expanded over time. Never let a noisy
  rule block the autonomous loop.
- **PII to third parties** — Sentry (and any SaaS that ingests exceptions/logs) needs a data-governance
  decision + scrubbing on an HRM platform. Prefer self-hosted where PII is involved.
- **Windows friction** — Dexter (Ruby/HypoPG) runs against Dockerized Postgres, not native PG18.
  MCP servers are hosted (Learn) or well-behaved stdio (Semgrep/NuGet); no `spawn ENOENT` risk expected.
- **License** — grafana/mcp-k6 is AGPL; confirm acceptable before wiring.
- **Don't fragment memory** — none of these introduce a competing memory store (that stays the vault +
  built-in agent memory).

## Suggested execution order

1. **This session:** Wave 0 + Wave 1 (all ~15 min each, zero risk) → one PR.
2. **Next:** Wave 2 (mechanical enforcement) → one PR per tool, each with its tuned ruleset, so noise is
   reviewable in isolation.
3. **Then:** Wave 3 (scheduled QA jobs) once Wave 2 is quiet.
4. **Decision gates:** book the Sentry (data-governance), Pact (only-if-recurring), Trivy
   (only-if-Docker-deploy), OpenSpec (only-if-story-quality) decisions explicitly rather than drifting.
