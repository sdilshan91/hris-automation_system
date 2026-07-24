# Observability Plan — OpenTelemetry + Grafana (LGTM)

**Status:** Partially implemented (Phase 1 shipped-dormant) · **Date:** 2026-06-26 · **Updated:** 2026-07-24 · **Owner:** platform
**Decisions locked:** local-dev-first / prod-ready design · Grafana **LGTM** backend · **OTel logs to Loki + keep Serilog file sink** · dashboards = **imported community + custom HRM** · **error tracking = self-hosted GlitchTip** ([ADR 2026-07-08](../vault/decisions/ADR-2026-07-08-saas-data-governance-posture.md))

---

## Implementation status (as of 2026-07-24)

Recorded after the **error-monitoring feasibility study** ([advisory-reports/error-monitoring-feasibility.md](advisory-reports/error-monitoring-feasibility.md), "GlitchTip vs Datadog, given we already use Serilog"). The three monitoring pillars — **logs · metrics+traces · error-tracking** — and what is actually covered in `src/` today:

| Pillar | Tool | Covered today | Remaining |
|---|---|---|---|
| **Logs** | Serilog (console + rolling file) | ✅ **Live** — `Program.cs` `UseSerilog`; `RequestId`/`TenantId`/`TenantSubdomain` enriched per request; daily `Logs/hrm-<date>.log` | Serilog→OTel sink to Loki (§1.4) — **not added**; file sink stays either way |
| **Metrics + traces** | OpenTelemetry | ✅ **Coded but DORMANT** — `OpenTelemetry.* 1.16.0` + `ObservabilityExtensions.AddObservability` wired at `Program.cs`; spans for AspNetCore + HttpClient + **Npgsql** (stable, not the pre-release EF instr.) + **Redis** (gated on the shared multiplexer) + `HRM.*`; runtime metrics. **Endpoint-gated: blank `OtlpEndpoint` ⇒ Console exporter only ⇒ exports nowhere.** | Stand up the LGTM backend (Phase 2/3) + set `OtlpEndpoint`; custom domain meters (§1.6); Hangfire spans. **$0 to leave dormant** |
| **Error tracking** | Self-hosted GlitchTip | ✅ **DECIDED** (accepted ADR) + **scaffolded** (`ops/glitchtip/docker-compose.yml`). **0% wired** — no `Sentry.*` package, no DSN config, no `@sentry/*` FE | **Phase 5 below** — wire the SDK + PII scrub + TenantId tag, run the compose |

> ⚠ **Plan-vs-shipped divergence (noted, not a defect):** the shipped OTel wiring uses **Npgsql's stable built-in DB spans**, not the pre-release `OpenTelemetry.Instrumentation.EntityFrameworkCore` this doc's §1.1 sketched; it adds **Redis** spans (not in the original sketch) and has **not** added the Serilog→OTel sink (§1.4), Hangfire instrumentation, or the custom `HRM.Metrics` meters (§1.6). Treat §1.1/§1.4/§1.6 as the *remaining* backlog, not as-built.

**Recommendation (from the feasibility study):** ship **GlitchTip first** (Phase 5 — highest value/effort, PII stays in-boundary), keep **Serilog as-is**, and leave the **OTel backend (Phase 2/3) deferred** until a concrete perf/latency question justifies the LGTM stack's RAM+ops cost. **Datadog rejected** — cloud egress of tenant-attributable PII contradicts the governance ADR. Full rationale + package/version evidence: [advisory-reports/error-monitoring-feasibility.md](advisory-reports/error-monitoring-feasibility.md).

---

## 0. Architecture (the seam that makes it prod-ready)

```
                                   ┌────────► Tempo    (traces)
HRM.Api  ──OTLP/gRPC:4317──►  OTel │
(traces+metrics+logs)        Collector ─────► Prometheus / Mimir (metrics)
                                   │
                                   └────────► Loki     (logs)
                                                  │
                                              Grafana (dashboards :3000)
```

**Why a Collector and not export direct to backends:** the app only ever knows *one* endpoint (the Collector OTLP port). Swapping local Tempo/Loki/Prometheus for a managed prod backend is a Collector-config change, **zero app redeploy**. That is the entire "prod-ready" payoff — keep the app→Collector seam clean.

Local runs the **same topology** as prod via a docker-compose `observability` profile, so dev and prod differ only in the Collector's exporter targets + retention.

> Quick-look alternative (not chosen): `grafana/otel-lgtm` bundles Collector+Tempo+Loki+Prometheus+Grafana in one image. Faster to first-trace, but its embedded Collector isn't the config you'd ship — you'd rewrite for prod. We use explicit services so the Collector config is real from day one.

---

## Phase 1 — Backend instrumentation (`HRM.Api` + `HRM.Infrastructure`)

### 1.1 NuGet packages
Add to **HRM.Api.csproj** (host wiring) — note corrected package IDs:

```xml
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.*" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.*" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.*" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.*" />
<PackageReference Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.*-*" />
<PackageReference Include="OpenTelemetry.Instrumentation.Hangfire" Version="1.*-*" />
<PackageReference Include="Serilog.Sinks.OpenTelemetry" Version="4.*" />
<PackageReference Include="Serilog.Enrichers.Span" Version="3.*" />
```

Notes:
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` is the correct OTLP exporter (the explore note's `OtlpProtobuf` name is wrong).
- **DB spans — pick ONE source to avoid double spans:** use `OpenTelemetry.Instrumentation.EntityFrameworkCore` (captures EF commands with `db.statement`). Do **not** also add `Npgsql.OpenTelemetry` `.AddNpgsql()` unless we want lower-level connection spans — that produces duplicate DB spans. EF-level is the right altitude for this app.
- EF Core + Hangfire instrumentation are currently pre-release (`1.*-*`); pin exact versions at implementation time.

### 1.2 New `AddObservability` extension
Mirror the project convention (single grouped extension, like `AddInfrastructure`). New file **`HRM.Api/Observability/ObservabilityExtensions.cs`**:

```csharp
public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration cfg)
{
    var o = cfg.GetSection("Observability");
    if (!o.GetValue("Enabled", true)) return services;

    var resource = ResourceBuilder.CreateDefault()
        .AddService(o["ServiceName"] ?? "hrm-api", serviceVersion: o["ServiceVersion"] ?? "1.0.0");
    var otlp = o["OtlpEndpoint"] ?? "http://localhost:4317";

    services.AddOpenTelemetry()
        .WithTracing(t => t
            .SetResourceBuilder(resource)
            .AddAspNetCoreInstrumentation(i => i.RecordException = true)
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation(i => i.SetDbStatementForText = true)
            .AddHangfireInstrumentation()
            .AddSource("HRM.*")                            // our manual ActivitySources
            .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(
                o.GetValue("SamplingRatio", 1.0))))
            .AddOtlpExporter(e => e.Endpoint = new Uri(otlp)))
        .WithMetrics(m => m
            .SetResourceBuilder(resource)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("HRM.*")                             // our custom metrics
            .AddOtlpExporter(e => e.Endpoint = new Uri(otlp)));

    return services;
}
```

Wire in **Program.cs after line 40** (`AddInfrastructure`), before MediatR:
```csharp
builder.Services.AddObservability(builder.Configuration);
```

### 1.3 Multi-tenant enrichment (the part that needs care)
Add tenant id as a **span tag** in `TenantResolutionMiddleware` (around line 145, where it already pushes Serilog LogContext):
```csharp
Activity.Current?.SetTag("tenant.id", tenant.Id);
Activity.Current?.SetTag("tenant.subdomain", tenant.Subdomain);
```

**HARD GUARDRAIL — tenant id is a trace/log attribute ONLY, never a metric label.** Per-tenant on a Prometheus label = cardinality explosion. Any custom metric must be bucketed by low-cardinality dims (plan tier, module, status code) — not `tenant_id`. This is called out again in §1.5 and the dashboard notes.

**Why this bites (the numbers):** Prometheus creates a *separate time series per unique label combination*, and labels multiply. A counter labelled `method`×`status`×`endpoint` can already be ~1,200 series; add a `tenant_id` with 10,000 values and it's **12M series from one metric** — not +10,000. Prometheus holds recent data in memory, so this surfaces as an **OOM a day or two after deploy**, not immediately. Division of signals: **metrics answer "is the platform healthy"; logs+traces answer "what happened to tenant 4471."**

- **Loki has the same trap, subtler:** its **stream labels are the index** — `tenant_id` as a Loki *label* is the same explosion. Put it in the **log line body** and filter with LogQL at query time.
- **Tempo is the exception:** high-cardinality attributes (incl. `tenant_id`) are genuinely fine — traces are keyed by trace ID and searched via TraceQL. This is where per-tenant/per-request detail belongs.

**RLS gives you NOTHING at the telemetry layer.** The Postgres tenant boundary we enforce so carefully does not extend to logs/traces — customer names in span attributes, PII in exception messages, query params in log lines will all cross it happily. Scrubbing at the collector (Phase 2 `attributes/scrub`) is therefore **mandatory, and a compliance question to raise before go-live, not after**. Same principle as the GlitchTip `BeforeSend` scrub (Phase 5) — every egress path needs it.

**Loki/Tempo have their own multi-tenancy (`X-Scope-OrgID`) — but you probably want it OFF.** That mechanism isolates data *between backend tenants* so you can hand per-customer dashboards out; with `auth_enabled: false` Loki runs single-tenant. For **our own ops team looking at one product, run single-tenant** and skip the header plumbing. Turn it on only if we actually expose per-customer dashboards. (Note: this is a *different* axis from our app's `tenant_id` — don't conflate the two.)

### 1.4 Serilog: add OTel sink + trace correlation, keep file sink
- Keep the **file sink exactly as-is** — `@test-runner` root-causes by reading `Logs/hrm-<date>.log`. Do not remove it (the user explicitly chose "keep file sink"; removing it breaks QA).
- Add `Enrich.WithSpan` (from `Serilog.Enrichers.Span`) so **TraceId/SpanId land in the file log too** — that's what lets QA correlate a file-log line to a Grafana trace.
- Add a `Serilog.Sinks.OpenTelemetry` sink → Collector OTLP → Loki. The existing `TenantId`/`tenant_id` LogContext properties flow through as log attributes automatically.

`appsettings.json` Serilog `WriteTo` gains:
```json
{ "Name": "OpenTelemetry",
  "Args": { "endpoint": "http://localhost:4317", "protocol": "grpc",
            "resourceAttributes": { "service.name": "hrm-api" } } }
```
and `Enrich` gains `"WithSpan"`.

### 1.5 Config section
Add to `appsettings.json` (after Hangfire section):
```json
"Observability": {
  "Enabled": true,
  "ServiceName": "hrm-api",
  "ServiceVersion": "1.0.0",
  "OtlpEndpoint": "http://localhost:4317",
  "SamplingRatio": 1.0
}
```
`appsettings.Development.json`: `SamplingRatio: 1.0` (sample everything in dev). Prod: lower (e.g. `0.1`) + tail-sampling at the Collector for errors/slow requests.

### 1.6 Custom domain instrumentation (enables the HRM dashboards)
Two small statics, registered as `ActivitySource`/`Meter` named under `HRM.*`:
- **Metrics** (Meter `HRM.Metrics`): counters/histograms for login success/failure, leave-request submitted/approved, attendance clock-in/out, Hangfire job outcome. Label dims: `module`, `outcome`, `plan` — **never `tenant_id`**.
- Auto-instrumentation already gives HTTP + DB + Hangfire spans; custom spans only where a domain operation spans multiple calls.

---

## Phase 2 — Local LGTM stack (docker-compose `observability` profile)

Extend the **existing** `docker-compose.yml` (don't fork it) with a profile so `docker compose --profile observability up` adds the stack; default `up` stays lean.

New services + config files under `docker/observability/`:

| Service | Image | Port | Config file |
|---|---|---|---|
| otel-collector | `otel/opentelemetry-collector-contrib` | 4317 (OTLP gRPC), 4318 (HTTP) | `collector-config.yaml` |
| tempo | `grafana/tempo` | 3200 | `tempo.yaml` |
| loki | `grafana/loki` | 3100 | `loki.yaml` |
| prometheus | `prom/prometheus` | 9090 | `prometheus.yaml` |
| grafana | `grafana/grafana` | 3000 | provisioning/ (datasources + dashboards) |

`backend` service gains `OBSERVABILITY__OTLPENDPOINT=http://otel-collector:4317` and `depends_on: otel-collector`.

**Collector config (`collector-config.yaml`)** — the prod-swappable seam:
```yaml
receivers:
  otlp: { protocols: { grpc: {endpoint: 0.0.0.0:4317}, http: {endpoint: 0.0.0.0:4318} } }
processors:
  batch: {}
  # PII scrub BEFORE egress — drop/redact sensitive attrs
  attributes/scrub:
    actions:
      - { key: db.statement, action: hash }      # don't leak raw SQL params
      - { key: user.email, action: delete }
      - { key: enduser.id, action: delete }
exporters:
  otlp/tempo:    { endpoint: tempo:4317, tls: {insecure: true} }
  prometheusremotewrite: { endpoint: http://prometheus:9090/api/v1/write }
  otlphttp/loki: { endpoint: http://loki:3100/otlp }
service:
  pipelines:
    traces:  { receivers: [otlp], processors: [attributes/scrub, batch], exporters: [otlp/tempo] }
    metrics: { receivers: [otlp], processors: [batch], exporters: [prometheusremotewrite] }
    logs:    { receivers: [otlp], processors: [attributes/scrub, batch], exporters: [otlphttp/loki] }
```
> **PII discipline (§ Critical Rule context):** logs/traces shipped to Loki/Tempo are a NEW data-egress path carrying employee PII. The `attributes/scrub` processor is mandatory before this ships beyond local — redact email/name/token/raw SQL params at the Collector. To prod, this same file just repoints exporters at the managed backends.

Grafana **datasource provisioning** auto-wires Tempo+Loki+Prometheus and enables **trace↔log correlation** (Tempo "Logs to Trace" via `trace_id`, Loki derived field on `TraceId`).

---

## Phase 3 — Dashboards (provisioned as code, not hand-clicked)

All dashboards live in `docker/observability/grafana/dashboards/*.json` and load via Grafana provisioning (version-controlled, reproducible).

**Imported / community (infra signals):**
1. **ASP.NET Core** — request rate, p50/p95/p99 latency, error %, active requests.
2. **.NET Runtime** — GC pauses, heap, thread-pool queue, exceptions.
3. **PostgreSQL / EF** — DB span duration, slow queries, connection use.

**Custom HRM (domain signals) — built from §1.6 meters:**
4. **Tenant Activity** — request volume & latency **broken down by `tenant.id` via traces/logs** (Tempo/Loki), *not* a Prometheus tenant label. Top-N busiest tenants, per-tenant error traces.
5. **Auth Health** — login success vs failure rate, lockout events, refresh-token rotations (ties to the auth findings ledger).
6. **Hangfire Jobs** — per-job success/failure/duration (AutoClockOutJob, TokenCleanupJob, …), last-run age, queue depth.
7. **Leave & Attendance Ops** — leave submitted/approved/rejected throughput, clock-in/out rate, anomaly counts.

---

## Phase 4 — Verification & rollout

1. `docker compose --profile observability up` → confirm Collector healthy.
2. `dotnet run --project HRM.Api` (native, **no debugger** per QA gotcha) pointed at Collector → hit a few endpoints + log in.
3. Grafana `:3000` → confirm: a trace in Tempo with `tenant.id` tag → its logs in Loki via trace_id → HTTP/DB/runtime metrics in Prometheus.
4. Confirm the **file sink still writes** `hrm-<date>.log` and the lines now carry `TraceId`.
5. Confirm **no `tenant_id` appears as a Prometheus label** (cardinality check).
6. Write an ADR-lite to `docs/vault/decisions/` recording the topology + the tenant-cardinality + PII-scrub rules.

---

## Phase 5 — Error tracking (GlitchTip) · **recommended: do this first**

Self-hosted, Sentry-API-compatible error tracker (accepted [ADR 2026-07-08](../vault/decisions/ADR-2026-07-08-saas-data-governance-posture.md); scaffolding already at `ops/glitchtip/`). **Additive to Serilog** — a new sink alongside console+file, not a replacement — and **complements** OTel (OTel = traces/metrics; GlitchTip = exception dedup/regression/alerting, which raw traces don't do well). ~1 day. Tracked as **US-PLT-006**.

### 5.1 Stand up the instance (already scaffolded)
`ops/glitchtip/docker-compose.yml` = `gt-postgres` (16, internal) + `gt-redis` (7, internal) + `migrate` + `web` (:8000) + `worker`. First run: rotate `ops/glitchtip/.env` secrets → `docker compose --env-file ops/glitchtip/.env -f ops/glitchtip/docker-compose.yml up -d` → register superuser at :8000 → create Org+Project → copy the **DSN**. Add `gt-pgdata` to the backup routine (new stateful store).

### 5.2 Backend SDK (`HRM.Api`)
Packages (pin at implement time; `Sentry.AspNetCore` 6.6.x supports .NET 10):
```xml
<PackageReference Include="Sentry.AspNetCore" Version="6.6.*" />
<PackageReference Include="Sentry.Serilog" Version="6.6.*" />
```
- `builder.WebHost.UseSentry(o => { o.Dsn = cfg["GlitchTip:Dsn"]; o.SendDefaultPii = false; o.SetBeforeSend(Scrub); })` — DSN blank-by-default in `appsettings.json` (`"GlitchTip": { "Dsn": "" }`), real value via user-secrets/env (Critical Rule #6). **Blank DSN ⇒ SDK inert** (same safe-default pattern as OTel's `OtlpEndpoint`).
- **PII scrub (`BeforeSend`) — mandatory:** strip request body, sensitive headers (`Authorization`, `Cookie`), query strings, and known PII fields before egress; `SendDefaultPii = false`.
- **TenantId tag:** in `BeforeSend`/scope set `tenant_id` + `tenant_subdomain` from the scoped `ITenantContext` (same source as the Serilog enrichers in `TenantResolutionMiddleware`) → issues filterable per tenant *without* shipping raw PII.
- Optional Serilog sink (`.WriteTo.Sentry`, `MinimumEventLevel = Error`) so `LogError` events also surface as GlitchTip issues — the Serilog file sink stays untouched.

### 5.3 Frontend SDK (optional 2nd slice)
`@sentry/angular` in `src/frontend`, DSN from `environment.ts`, a `beforeSend` scrub mirroring the backend, tenant tag from the existing `tenant/` subdomain signal. **Verify the SDK major against Angular 20 and pin** (per the `angular-developer` skill's version-check rule).

### 5.4 QA verification (report-only, `@test-runner`)
- Throw a deliberate test exception behind a dev-only endpoint → confirm the issue appears in GlitchTip with the correct stack + release.
- Confirm the event carries `tenant_id`/`tenant_subdomain` tags and **does NOT** carry request body / `Authorization` / email PII (scrub proof).
- Confirm blank DSN ⇒ nothing ships (inert default).
- Cross-tenant check: two tenants' errors are tagged distinctly (tenant-isolation extends to telemetry).

---

## Phase 6 — Frontend RUM (Grafana Faro) — *deferred with the LGTM backend*

Prometheus/Loki/Tempo have **no concept of a browser** — our Angular SPA is a blind spot in the stack above. **Grafana Faro** fills it: the Faro Web SDK captures real-user performance, errors, logs, and client-side traces from the browser and (open-source path) ships them via **Alloy** into the same LGTM backend. The SDK is framework-agnostic (Angular setup differs slightly from React; same instrumentation API).

- **⚠ Overlaps GlitchTip (Phase 5) on frontend errors.** Faro's error capture and `@sentry/angular`'s do the same job. **Pick ONE for browser exceptions** — do not run both and triage duplicates. Default: GlitchTip owns errors (it's the decided error tracker); adopt Faro **only if/when** we want browser *RUM/perf* signals, and if so, disable Faro's error capture (or drop the FE Sentry slice).
- Deferred alongside Phase 2/3 — no point standing up Faro before the LGTM backend exists to receive it.

---

## Effort & sequencing
- **Phase 5 (GlitchTip) is the recommended first slice** — highest value/effort, self-contained, and the decision + scaffolding already exist. Do it before the LGTM backend.
- **When the LGTM backend is built, add signals in order: Prometheus+Grafana → Loki → Tempo.** ASP.NET Core emits useful meters out of the box (request rate/latency/error-rate for ~zero instrumentation) + `postgres_exporter` for pool saturation/slow queries → cheapest first value. Loki next (structured .NET logs are cheap to ship). **Tempo last, and only if actually distributed** — for a monolithic API on one Postgres, traces mostly restate what the metrics already showed. All three run single-binary on filesystem storage in one Compose file; move to S3-compatible object storage when retention hurts.
- **Phase 1** (backend) and **Phase 2/3** (stack + dashboards) are largely independent — can run in parallel (backend-dev on instrumentation; infra/compose + dashboards alongside). They converge at Phase 4 verification.
- Suggested order if serial: 2 → 1 → 3 → 4 (stand up the stack first so you can watch telemetry appear as you instrument).

## Open risks / watch-items
- EF Core + Hangfire OTel instrumentation are **pre-release** — pin versions; if either is unstable, fall back to manual `ActivitySource` spans for jobs. (As-shipped we sidestepped EF instr. entirely — Npgsql stable spans; see the Implementation-status note at the top.)
- `Serilog.Sinks.OpenTelemetry` duplicates logs into Loki *and* file — that's intended here, just sized accordingly.
- 8 GB dev box: the LGTM stack + app + Postgres is memory-hungry (see [docker-local-stack] memory note). The `observability` profile keeps it opt-in; consider the single `otel-lgtm` image locally if RAM is tight. **Honest ops trade vs GlitchTip:** the full LGTM(+Faro) path is **4–5 services to operate** vs GlitchTip's one — you're buying metrics+tracing GlitchTip can't give, and paying in ops surface. This is *why* the recommendation defers it until a perf question justifies it.
- **⚠ Version traps — most tutorials are stale (verify before copying a Compose example):**
  - **Grafana Agent** reached **EOL 2025-11-01**; **Promtail** reached **EOL 2026-03-02** → use **Grafana Alloy** for new deployments. Any guide showing either is pre-consolidation.
  - Loki **Simple Scalable Deployment (SSD)** mode is deprecated and slated for removal in **Loki 4.0** — several popular Compose examples still use it.
  - **Collector choice isn't settled:** §0/Phase 2 spec the **OTel Collector**, but **Faro (Phase 6) needs Alloy's `faro.receiver`**. If we adopt Faro, standardize on **Alloy** as the single collector (it speaks OTLP *and* Faro) rather than running both. Decide at Phase 2 build time.
