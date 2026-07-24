---
id: US-PLT-006
module: Platform / Cross-Cutting
priority: Should Have
persona: System Admin / Platform Operator
status: draft
created: 2026-07-24
sprint: backlog
acceptance_criteria_count: 7
---

# US-PLT-006: Error Tracking via Self-Hosted GlitchTip (Sentry-API-Compatible)

> **Net-new 2026-07-24, from the error-monitoring feasibility study.** This is **execution, not
> deliberation** — the decision is already made and recorded in an **accepted** ADR
> ([`ADR-2026-07-08 — SaaS data-governance posture`](../../vault/decisions/ADR-2026-07-08-saas-data-governance-posture.md),
> **Decision 1**): adopt **self-hosted GlitchTip** — *not* SaaS Sentry, and *not* Datadog (rejected on
> cloud PII egress) — with **SDK-level PII scrubbing** (`beforeSend` strips request bodies / PII fields;
> `SendDefaultPii = false`). Docker-compose scaffolding already exists at `ops/glitchtip/`; the SDK wiring
> is **0% done** (no `Sentry.*` package, no DSN config). Technical basis + minimal integration sketch:
> [`docs/Architecture/advisory-reports/error-monitoring-feasibility.md`](../../Architecture/advisory-reports/error-monitoring-feasibility.md).
> This story **formalizes the decided scope — it introduces no new scope.**

## 1. Description
**As a** System Admin / Platform Operator,
**I want** unhandled exceptions from the API captured, deduplicated, and alerted on in a **self-hosted**
GlitchTip instance — tagged by tenant and scrubbed of PII before they ever leave the process,
**So that** I can triage production errors per tenant, per release, without shipping regulated HR PII outside
our trust boundary and without losing the existing Serilog file log that QA uses for `RequestId` root-cause.

## 2. Preconditions
- The GlitchTip stack (`ops/glitchtip/docker-compose.yml`: `gt-postgres`, `gt-redis`, `migrate`, `web`, `worker`)
  is running; a superuser, Org, and Project have been registered at `:8000` and a **project DSN** issued.
- The DSN is available to the API **only** via user-secrets / environment variable (never committed — Critical Rule #6).
- Serilog structured logging with per-request `TenantId` / `TenantSubdomain` / `RequestId` enrichment is already
  in place (`TenantResolutionMiddleware` pushes these into the `LogContext` on every request — the PII surface).
- Backend WIP has settled (the ADR deferred execution "until backend WIP settles" — confirm before scheduling).

## 3. Acceptance Criteria (IEEE 830 §3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The API is running with a valid `GlitchTip:Dsn` configured | An unhandled/thrown exception occurs during a request | The exception is captured in GlitchTip with its **stack trace** and the **release/version**, and is tagged with `tenant_id` and `tenant_subdomain` so issues are filterable per tenant. |
| AC-2 | An exception event is about to be sent | `BeforeSend` runs on the outgoing event | The **request body**, the **`Authorization` header**, **cookies/session** data, and **known PII fields** (email, national ID) are stripped from the event; `SendDefaultPii = false` is enforced so no default PII is attached. |
| AC-3 | `GlitchTip:Dsn` is **blank** (the shipped default) | The application starts and runs, and exceptions occur | The SDK is **inert** — no network calls are made, no events are queued, and application behaviour is unaffected (safe-by-default). The DSN is supplied only via user-secrets/env, never from the committed `appsettings.json`. |
| AC-4 | GlitchTip is configured and receiving events | Exceptions flow to it | All exception telemetry stays **within our trust boundary** (self-hosted only; no third-party cloud egress), and the GlitchTip sink **composes additively** with the existing Serilog **console + file** sinks — the daily rolling file (`Logs/hrm-<date>.log`) remains the QA/`RequestId` root-cause log. |
| AC-5 | The Serilog + ASP.NET Core pipeline is wired | The API processes requests and logs events | GlitchTip receives events via a **Serilog sink at `Error` level** plus the **ASP.NET Core integration** (`UseSentry`); the existing OpenTelemetry wiring (`ObservabilityExtensions`) is left in place and **complementary — not replaced**. |
| AC-6 *(optional / phase-2)* | The Angular SPA is configured with `@sentry/angular` and a frontend DSN | A client-side error is thrown | It is captured in GlitchTip with a `beforeSend` scrub **mirroring the backend** (request/PII stripping) and a `tenant_id` / `tenant_subdomain` tag derived from the `tenant/` subdomain signal. *(Clearly optional — a separate phase-2 slice; not required to close the backend value.)* |
| AC-7 | GlitchTip is running in our infra | The backup/retention routine executes | The GlitchTip Postgres volume (`gt-pgdata`) is included in the backup routine so error history survives a restore. |

## 4. Functional Requirements (IEEE 830 §3.2)
- FR-1: Add `Sentry.AspNetCore` and `Sentry.Serilog` to `HRM.Api.csproj` (pin exact versions at implement time;
  `Sentry.AspNetCore 6.6.x` supports .NET 10). No other project references the SDK.
- FR-2: Add a Serilog **`WriteTo.Sentry`** sink *alongside* the existing console + file sinks in the `UseSerilog`
  configuration, with `MinimumEventLevel = Error` and `SendDefaultPii = false`, reading the DSN from `GlitchTip:Dsn`.
- FR-3: Wire ASP.NET Core integration via `builder.WebHost.UseSentry(...)` with `SendDefaultPii = false` and a
  `SetBeforeSend` hook.
- FR-4: In `BeforeSend`, strip the request body, `Authorization` header, cookies/session, query parameters, and
  known PII fields (email, national ID) from every event before it is sent (AC-2).
- FR-5: In `BeforeSend`/scope, set `tenant_id` and `tenant_subdomain` tags from the scoped `ITenantContext` so
  every issue is tenant-attributable (AC-1).
- FR-6: Attach the application **release/version** to captured events so regressions can be tracked per release (AC-1).
- FR-7: Add config key `"GlitchTip": { "Dsn": "" }` to `appsettings.json` as a **blank placeholder**; the real
  value is provided only via user-secrets/env (AC-3, Critical Rule #6).
- FR-8: When `GlitchTip:Dsn` is blank, the SDK must initialise inert (no network activity) — verify the guard (AC-3).
- FR-9 *(optional / phase-2)*: Add `@sentry/angular` to `src/frontend`, DSN from `environment.ts`, with a
  `beforeSend` scrub mirroring the backend and a tenant tag from the subdomain signal (AC-6).
- FR-10: Add the GlitchTip Postgres volume to the backup/retention routine (AC-7).

## 5. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1 (Security / Privacy): No PII (request bodies, `Authorization`/cookies, email, national ID) may leave the
  process in any captured event; `SendDefaultPii = false` is mandatory. Scrubbing is a **hard condition** of the ADR.
- NFR-2 (Data residency / Multi-tenancy): All exception telemetry remains **in-boundary** (self-hosted GlitchTip);
  no third-party sub-processor, no DPA/residency obligation triggered. Every event is tenant-attributable via tags.
- NFR-3 (Availability / Safety): A blank/misconfigured DSN, or an unreachable GlitchTip instance, must **never**
  affect request handling or crash the app (fail-safe, inert-by-default).
- NFR-4 (Performance): The Serilog sink captures at `Error` level only (not per-request), keeping ingestion volume
  and overhead low; capture is asynchronous and must not add measurable request latency.
- NFR-5 (Operability): Self-hosting adds 4 containers (Postgres, Redis, web, worker) — internal-only except `:8000`;
  the Postgres volume is backed up (AC-7). Secrets in `ops/glitchtip/.env` are rotated before first run.

## 6. Business Rules
- BR-1: Error-tracking telemetry for a data-controller HR SaaS **must** stay inside the trust boundary — cloud
  error trackers (SaaS Sentry, Datadog) are **rejected** per ADR-2026-07-08 Decision 1.
- BR-2: PII scrubbing (`beforeSend` + `SendDefaultPii = false`) is **non-negotiable** — it is the accepted
  condition under which self-hosting was approved.
- BR-3: The DSN is a secret — sourced from user-secrets/env only, never committed (Critical Rule #6).
- BR-4: GlitchTip is **additive** — it must not replace or degrade the existing Serilog console/file sinks or the
  OpenTelemetry wiring; the file sink stays the authoritative QA `RequestId` root-cause log.
- BR-5: Every captured issue must carry `tenant_id` + `tenant_subdomain` tags so triage is tenant-scoped.

## 7. Data / Config Requirements
- **Config keys:** `GlitchTip:Dsn` (string, default `""`). Real value via user-secrets `GlitchTip:Dsn` or env.
- **Event tags (outgoing):** `tenant_id`, `tenant_subdomain`, release/version.
- **Scrubbed from every event (must NOT be sent):** request body, `Authorization` header, cookies/session,
  query parameters, `email`, national ID.
- **Compose / infra:** `ops/glitchtip/docker-compose.yml` (existing) — `gt-postgres`, `gt-redis`, `migrate`,
  `web` (`:8000`), `worker`; `ops/glitchtip/.env` (gitignored; rotate before first run); volume `gt-pgdata`
  added to backups.
- **Packages:** `Sentry.AspNetCore`, `Sentry.Serilog` (backend); `@sentry/angular` (optional FE).

## 8. UI/UX Notes
- No end-user UI in the backend slice — GlitchTip's own web console (`:8000`) is the operator-facing surface
  (issue list, per-tenant filtering by tag, release regressions, alerts).
- Optional FE slice (AC-6/FR-9) has no visible UI beyond client-error capture; no user-facing change.

## 9. Dependencies
- **ADR-2026-07-08 Decision 1** (accepted) — the source decision (self-hosted GlitchTip + mandatory scrubbing).
- **Feasibility study** `docs/Architecture/advisory-reports/error-monitoring-feasibility.md` — technical sketch.
- `TenantResolutionMiddleware` — supplies the `TenantId`/`TenantSubdomain` used for event tags.
- **US-PLT-004** (Observability & platform NFRs, OTel) — **complementary, not a dependency**: OTel handles
  traces/metrics; GlitchTip handles exception aggregation. They co-exist; neither replaces the other.
- `ops/glitchtip/` compose scaffolding (already present).
- Downstream: unblocks the Sentry MCP for `@browser-debugger` / `/fault-diagnosis` (TOOLING-ADOPTION-PLAN #11).

## 10. Assumptions & Constraints
- **Assumption:** `Sentry.AspNetCore 6.6.x` (and matching `Sentry.Serilog`) supports .NET 10 — verify and pin at
  implement time (feasibility: High confidence).
- **Assumption:** `@sentry/angular` current major is compatible with Angular 20 — verify at implement time
  (feasibility: Medium confidence). This is why FE is scoped **optional / phase-2**.
- **Constraint:** Datadog and any third-party-cloud error tracker are out of scope (rejected by ADR on PII egress).
- **Constraint:** Backend WIP must have settled before scheduling (ADR deferral condition).
- **Constraint:** No new scope beyond the decided integration sketch — this story formalizes, it does not re-decide.

## 11. Test Hints
- Throw a deliberate unhandled exception behind a tenant subdomain → confirm it appears in GlitchTip with a full
  stack trace, the release/version, and `tenant_id` + `tenant_subdomain` tags matching that tenant (AC-1).
- Trigger an exception on a request carrying a body, an `Authorization` header, cookies, and an email/national-ID
  field → inspect the captured event and confirm **none** of those values are present; confirm `SendDefaultPii`
  is false (AC-2).
- Run the app with a **blank** `GlitchTip:Dsn` and force exceptions → confirm zero network calls to GlitchTip and
  no behavioural change / no crash (AC-3); confirm the committed `appsettings.json` DSN is blank (no secret leaked).
- Confirm the Serilog console + file sinks still emit (file `Logs/hrm-<date>.log` still written) with GlitchTip
  enabled — additive, not replacing (AC-4); confirm OTel wiring is unchanged (AC-5).
- Confirm only `Error`-level events reach GlitchTip (an `Information`/`Warning` log does not) (AC-5).
- *(Optional)* FE: throw a client-side error with `@sentry/angular` configured → confirm capture with mirrored
  scrub + tenant tag from the subdomain signal (AC-6).
- Ops: confirm `gt-pgdata` is enumerated by the backup routine (AC-7).
