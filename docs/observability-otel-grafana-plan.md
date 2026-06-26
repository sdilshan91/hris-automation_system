# Observability Plan — OpenTelemetry + Grafana (LGTM)

**Status:** Proposed · **Date:** 2026-06-26 · **Owner:** platform
**Decisions locked:** local-dev-first / prod-ready design · Grafana **LGTM** backend · **OTel logs to Loki + keep Serilog file sink** · dashboards = **imported community + custom HRM**

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

## Effort & sequencing
- **Phase 1** (backend) and **Phase 2/3** (stack + dashboards) are largely independent — can run in parallel (backend-dev on instrumentation; infra/compose + dashboards alongside). They converge at Phase 4 verification.
- Suggested order if serial: 2 → 1 → 3 → 4 (stand up the stack first so you can watch telemetry appear as you instrument).

## Open risks / watch-items
- EF Core + Hangfire OTel instrumentation are **pre-release** — pin versions; if either is unstable, fall back to manual `ActivitySource` spans for jobs.
- `Serilog.Sinks.OpenTelemetry` duplicates logs into Loki *and* file — that's intended here, just sized accordingly.
- 8 GB dev box: the LGTM stack + app + Postgres is memory-hungry (see [docker-local-stack] memory note). The `observability` profile keeps it opt-in; consider the single `otel-lgtm` image locally if RAM is tight.
