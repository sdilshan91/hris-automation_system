# Feasibility Study — Error Tracking / Monitoring for HRM SaaS

**Type:** Advisory / feasibility (REPORT-ONLY) · **Date:** 2026-07-24 · **Author:** `@principal-advisor`
**Question asked:** "How do we plan to integrate GlitchTip? We already use Serilog. Can we use Datadog?"

---

## TL;DR verdict

**This is not a greenfield "should we?" decision — it was already made and recorded.** An **accepted** ADR
(`docs/vault/decisions/ADR-2026-07-08-saas-data-governance-posture.md`, Decision 1) chose **self-hosted
GlitchTip** over SaaS Sentry *and* implicitly over any third-party-cloud tracker (Datadog included) on
**PII / data-residency grounds**. The docker-compose scaffolding already exists at `ops/glitchtip/`. What's
missing is the SDK wiring.

| Option | Verdict | One-line reason |
|---|---|---|
| **Self-hosted GlitchTip** (error tracking) | **GO** | Already decided (accepted ADR), scaffolding exists, keeps PII in-boundary. Only the SDK wiring remains. |
| **Datadog** (full observability SaaS) | **Consider-later / effectively NO for now** | Cloud egress of tenant PII contradicts the accepted governance ADR; per-host + per-GB cost unjustified at this stage. |
| **OpenTelemetry + OTLP backend** (observability) | **GO — and already partially shipped** | Already wired (endpoint-gated) in `ObservabilityExtensions.cs`; complements GlitchTip, doesn't compete. |

---

## What the code actually shows (evidence)

**Stack & Serilog (verified):**
- All backend projects target **`net10.0`** (`HRM.Api.csproj:4`). ASP.NET Core 10 confirmed.
- Serilog wired via `builder.Host.UseSerilog(...)` at `src/backend/HRM.Api/Program.cs:38-45`;
  `Enrich.FromLogContext()`, `Enrich.WithProperty("Application","HRM.Api")`. Request logging at `Program.cs:549`.
- Sinks today = **Console + rolling File** (`Logs/hrm-.log`, daily, 31-file retention). No network/aggregation sink.
- **Per-request tenant enrichment**: `TenantResolutionMiddleware.cs:139-142` pushes `TenantId`,
  `TenantSubdomain`, `tenant_id`, `tenant_subdomain` into `LogContext`. **This is the PII surface** — any
  sink/error tracker inherits tenant identity on every event.

**Existing observability (verified — important):**
- **OpenTelemetry is already installed and wired**, not just referenced. `HRM.Api.csproj:38-48` pins
  `OpenTelemetry.*` `1.16.0`. `Program.cs:61-64` calls `AddObservability(...)`
  (`HRM.Api/Observability/ObservabilityExtensions.cs`).
- **Endpoint-gated / safe-by-default**: OTLP export only when `OpenTelemetry:OtlpEndpoint` (or
  `OTEL_EXPORTER_OTLP_ENDPOINT`) is set, else Console exporter only (default blank).
- Design doc: `docs/Architecture/observability-otel-grafana-plan.md` (Proposed) — Grafana **LGTM** behind an
  OTel Collector; Serilog file sink deliberately kept as the QA root-cause log.

**GlitchTip — decision + scaffolding already exist (verified):**
- `ADR-2026-07-08-saas-data-governance-posture.md`, **status: accepted**, **Decision 1**: adopt
  **self-hosted GlitchTip** — not SaaS Sentry — with **SDK-level PII scrubbing** (`beforeSend` strips request
  bodies / PII fields; `sendDefaultPii=false`). Execution explicitly **deferred "until backend WIP settles."**
- `docs/DEV/TOOLING-ADOPTION-PLAN.md:27` — item #11 marked **✅ DECIDED (self-hosted)**, ~1 day, Wave 4.
- `ops/glitchtip/docker-compose.yml` **already exists**: `gt-postgres` (postgres:16, internal), `gt-redis`
  (redis:7, internal), `migrate`, `web` (:8000 exposed), `worker`. `.env.example` present; real `.env` gitignored.

**What is NOT done (verified absent):**
- **No `Sentry.*` package** in any `.csproj`. No `UseSentry`/`AddSentry`/`WriteTo.Sentry`. No
  `Glitchtip`/`Sentry`/`Dsn` config key. No `@sentry/*` in the frontend. **The SDK layer is entirely unwired.**

**ADR-drift classification:** Decision 1 → **drifted (planned-not-yet-implemented)** — the intended-but-not-
caught-up case, consistent with the ADR's own "deferred until backend WIP settles."

---

## Category framing: error tracking ≠ full observability

- **GlitchTip = error tracker.** Ingests exceptions, dedups into issues, tracks regressions/resolution,
  alerts. Sentry-API-compatible, open source, self-hostable. *"What's throwing, how often, since which
  release, for which tenant?"*
- **Datadog = full observability SaaS.** APM + infra metrics + logs + RUM + synthetics — at the cost of
  shipping all telemetry to Datadog's cloud.
- **OpenTelemetry = vendor-neutral instrumentation**, not a backend — the wire format/SDK that can feed
  Grafana LGTM, Datadog, or any OTLP endpoint.

The real decision is **not** "GlitchTip vs Datadog" — it's *"error tracking, full observability, or both — and
where is the data allowed to live?"* This codebase already split it correctly: OTel + Grafana LGTM for
traces/metrics/logs, GlitchTip for errors. Both self-hosted, both in-boundary.

---

## Option 1 — Self-hosted GlitchTip (PRIMARY ASK) — **GO**

**Fit:** Excellent (.NET 10 + Serilog + Angular 20, self-hosted Docker). **Cost: S (~1 day; scaffolding exists).**

**Why it's right (and already decided):**
1. **PII stays in our trust boundary.** Exceptions carry request bodies + tenant identity. Shipping raw
   exceptions to a third party triggers DPA + sub-processor disclosure + data-residency obligations.
2. **Composes with Serilog additively.** GlitchTip is a *new sink alongside* console+file — the file sink
   stays the QA/`RequestId` root-cause log.
3. **Sentry-API compatibility** → the mature Sentry .NET SDK works unchanged with a GlitchTip DSN. Confidence: High.

**Version reality (verified 2026-07-24):**
- `Sentry.AspNetCore` **6.6.0** (2026-05-28) **explicitly adds .NET 10 support**. Confidence: High.
- `Sentry.Serilog` on the same version line. Confidence: High; pin exact version at implement time.
- `@sentry/angular` current browser SDK — verify major vs installed Angular 20 and pin. Confidence: Medium.

**Operational cost (honest):** self-hosting adds another **Postgres + Redis + web + worker** (4 containers) to
run and back up. Modest (internal-only DB/Redis, only :8000 exposed) but not zero.

**Conditions:** SDK-level PII scrubbing is **mandatory** (`beforeSend` + `sendDefaultPii=false`); rotate
`.env` secrets before first run.

---

## Option 2 — Datadog — **Consider-later / effectively NO for now**

**Fit:** Technically fine (`Serilog.Sinks.Datadog.Logs` + `dd-trace` .NET APM, both .NET 10). Blocker isn't technical.

1. **Cloud egress of tenant PII contradicts the accepted governance ADR** — Datadog becomes a sub-processor
   for regulated HR data (DPA + per-tenant disclosure + US/EU residency answer). Confidence: High.
2. **Per-host + per-GB cost compounds.** Rough list: Infra ~$15/host/mo, APM ~$31/host/mo, Logs ~$0.10/GB
   ingested + retention. Low-hundreds/month for a small SaaS, rising with traffic. Confidence: Medium (verify at decision-time).
3. **Category-overkill for the current need** (error tracking). Datadog's APM/RUM value justifies its price
   and PII tradeoff only at scale.

**Revisit** only if scale demands managed APM/RUM/infra correlation *and* the DPA/residency obligations can be
met (region-pinned + aggressive scrubbing). Parks at the **decision-gate**.

---

## Option 3 — OpenTelemetry + Grafana LGTM — **GO, already partially shipped**

Not hypothetical: **OTel is already wired** (endpoint-gated, safe-by-default) with a locked plan doc
(`observability-otel-grafana-plan.md`). Remaining work: stand up the Collector + LGTM compose profile (app
side is done). **Cost: S.**

The Collector seam means the app knows one OTLP endpoint; swapping local Tempo/Loki/Prometheus for a managed
backend later is a Collector-config change with **zero app redeploy** — the smarter long-term hedge than
locking into a vendor SDK now.

**Crucial:** OTel/LGTM and GlitchTip are **complementary, not either/or**. OTel = traces+metrics(+logs);
GlitchTip = exception aggregation/dedup/alerting. Run both, both self-hosted (which this project already decided).

---

## Multi-tenant PII / compliance — the genuine differentiator

| | Self-hosted GlitchTip | OTel + Grafana LGTM | Datadog |
|---|---|---|---|
| Where exceptions/telemetry live | **Our infra** | **Our infra** | **Third-party cloud** |
| Sub-processor / DPA obligation | None | None | **Yes** |
| Per-tenant data-residency answer | In-boundary | In-boundary | **Region-dependent, must disclose** |
| Data-residency risk | Low | Low | **High** |

Every exported event is tenant-attributable (`TenantId`/`TenantSubdomain`/`RequestId` enrichment). A feature
for in-boundary tools (fast per-tenant triage); a liability for cloud egress. This asymmetry is exactly why
the ADR chose self-hosting.

---

## Candid bottom line

**GlitchTip now; defer Datadog/full-cloud-observability until scale justifies it — and even then prefer the
OTel Collector seam over a vendor SDK.** (1) The decision is already recorded in an accepted ADR and scaffolded
— this is execution, not deliberation; (2) HR PII must not leave the trust boundary, which rules Datadog out
on compliance, not just cost; (3) the project already runs the right two-layer split. The only real work is
**wiring the SDKs the ADR already called for**.

---

## If we do GlitchTip — minimal integration sketch (for a follow-up `/implement-story`)

**1. Packages** (`HRM.Api.csproj`) — pin exact versions at implement time:
```
Sentry.AspNetCore   (6.6.x — .NET 10 supported)
Sentry.Serilog      (matching line — the Serilog sink)
```

**2. Serilog sink** — add *alongside* existing console+file sinks in the `UseSerilog` config (`Program.cs:38`):
```
.WriteTo.Sentry(o => {
    o.Dsn = cfg["GlitchTip:Dsn"];
    o.MinimumEventLevel = LogEventLevel.Error;
    o.SendDefaultPii = false;
})
```
Config key `appsettings.json`: `"GlitchTip": { "Dsn": "" }` (blank placeholder; real value via
user-secrets/env — never committed, per Critical Rule #6).

**3. ASP.NET Core integration + PII scrub + TenantId tag** — `builder.WebHost.UseSentry(o => {...})`:
- `o.SendDefaultPii = false;`
- `o.SetBeforeSend((evt, hint) => { /* strip request body, sensitive headers, query params, known PII fields */ return evt; });`
- Tenant tagging in `BeforeSend`/scope: `evt.SetTag("tenant_id", <ITenantContext>)` + `tenant_subdomain` —
  issues filterable per tenant without shipping raw PII.

**4. docker-compose** — already at `ops/glitchtip/docker-compose.yml`. First run: rotate `ops/glitchtip/.env`
→ `docker compose --env-file ops/glitchtip/.env -f ops/glitchtip/docker-compose.yml up -d` → register
superuser at :8000 → create Org+Project → copy DSN into user-secrets as `GlitchTip:Dsn`.

**5. Frontend (optional 2nd slice)** — `@sentry/angular` in `src/frontend`, DSN from `environment.ts`,
`beforeSend` scrub mirroring backend, tenant tag from the `tenant/` subdomain signal. Verify SDK major vs Angular 20.

**6. Backups** — add the GlitchTip Postgres volume (`gt-pgdata`) to the backup/retention routine.

---

## OUT-OF-LANE (feeds `/auto-heal`)

```
type:        GAP
severity:    MED
where:       ADR-2026-07-08 (Decision 1) vs src/backend/HRM.Api (no Sentry.* refs, no DSN config)
what:        GlitchTip is an accepted decision with compose scaffolding, but the .NET + Angular SDK wiring +
             PII scrubbing the ADR mandates is entirely unimplemented.
why_oo_lane: /advisor is report-only; wiring SDKs edits src/ — a scoped implementation task.
suggested:   /implement-story to execute the integration sketch. ADR deferred this "until backend WIP settles";
             confirm WIP has settled before scheduling.
blocks:      Sentry MCP for @browser-debugger / /fault-diagnosis (TOOLING-ADOPTION-PLAN #11).
```

## Gaps
- Exact `@sentry/angular` major vs Angular 20 not pinned — verify at implement time (Confidence Medium).
- Datadog pricing is order-of-magnitude — re-verify at any future decision-gate (Confidence Medium).

## Sources
- [Sentry.AspNetCore on NuGet (6.6.0, .NET 10)](https://www.nuget.org/packages/Sentry.AspNetCore/)
- [GlitchTip C# SDK docs](https://glitchtip.com/sdkdocs/csharp/)
- [Sentry for .NET — Serilog integration](https://docs.sentry.io/platforms/dotnet/guides/serilog)
