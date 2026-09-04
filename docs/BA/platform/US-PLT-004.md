---
id: US-PLT-004
module: Platform / Cross-Cutting
priority: Should Have
persona: System Admin / Platform Operator
status: ready
created: 2026-07-06
updated: 2026-09-04
sprint: backlog
acceptance_criteria_count: 5
---

# US-PLT-004: Observability & Platform NFRs (OTel, Health, Per-Tenant Usage, SLOs)

> **AUTHORED FROM SHIPPED CODE, 2026-09-04 (F4 / GAP-030).** This story was a stub written 2026-07-06;
> the code shipped 2026-07-30, with the per-tenant API-call counter following on 2026-07-31 (commit
> `b9906626`). Sections §4–§10 are **reverse-engineered from what is actually in `src/`**, not from the
> stub's intent — where the two disagree, the code wins.
>
> **Three of the five acceptance criteria are NOT met.** They are recorded as unmet in §3.1 with the
> specific reason, and the matching requirements are marked `NOT IMPLEMENTED` in §4. Nothing below
> describes a control that does not exist.
>
> ⚠ **Do not author requirements from the in-code comments in this area.** Six comments in
> `PlatformMonitoringService.cs`, `MonitoringDtos.cs`, `appsettings.json` and `monitoring.models.ts`
> describe fields as "always null" / "always empty" / "DEFERRED" on the line above a real computed
> assignment. They are stale (filed as **ISSUE-461**). Trust the code.

## 1. Description
**As a** System Admin / Platform Operator,
**I want** real observability — distributed tracing, metrics, health probes, and per-tenant usage counters —
**So that** platform monitoring reflects real system state instead of hard-coded nulls, and SLOs can be measured.

## 2. Preconditions
- Serilog structured logging with RequestId/TenantId enrichment already in place (baseline).
- PostgreSQL reachable — it backs `health_probe`, `tenant_api_usage` and `tenant_latency_bucket`, and is
  the hard readiness dependency.
- Hangfire recurring-job storage available (the 5-minute health probe is a Hangfire recurring job).

## 3. Acceptance Criteria (IEEE 830 §3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The API is running | A request is processed | An OpenTelemetry trace + metrics are emitted (request latency, error count) to a configured exporter/store. |
| AC-2 | An orchestrator/probe checks the service | It calls `/health/live` and `/health/ready` | Liveness and readiness (DB/Redis/dependencies) are reported accurately. |
| AC-3 | The System Admin opens monitoring (US-ADM-002) | The page loads | Real error-rate %, P95 latency, and SLA/uptime values are shown (no longer hard-coded null). |
| AC-4 | Usage accrues per tenant | Usage is queried | Per-tenant counters (API calls, storage, emails) are recorded and exposed (feeds US-ADM-012 enforcement). |
| AC-5 | An SLO is defined (e.g. login p95) | Traffic flows | The SLO is instrumented and measurable (ties to ISSUE-203). |

### 3.1 AC verdicts against shipped code (verified 2026-09-04)

| AC | Verdict | Evidence / reason |
|----|---------|-------------------|
| AC-1 | **UNMET — producer side only** | The SDK, both exporters and the sampler are wired (`HRM.Api/Observability/ObservabilityExtensions.cs:164-223`), but **nothing in this repository can receive OTLP**. An exhaustive sweep of `docker-compose.yml`, `docker-compose.{dev,debug,tls}.yml`, `ops/` and `local-dev/` finds **no OTel Collector, Tempo, Jaeger, Zipkin, Prometheus, Grafana or Loki**, and there is no k8s or Helm manifest anywhere in the repo. The only shipped telemetry backend is GlitchTip (`ops/glitchtip/`), which is error tracking, not traces/metrics. The only file matching `*otel*`/`*grafana*` is the **plan document** `docs/Architecture/observability-otel-grafana-plan.md`. The app can emit; there is no store, and no `/metrics` endpoint to scrape either. |
| AC-2 | **MET** | `/health/live` and `/health/ready` mapped with tag predicates (`Program.cs:799-806`); checks registered `Program.cs:108-131`. See FR-11..FR-15 for the deliberately asymmetric failure semantics. |
| AC-3 | **PARTIAL** | The **backend computes real values** — `AggregateErrorRatePercent` (`PlatformMonitoringService.cs:130-137`, assigned `:161`), platform `P95LatencyMs` (`:140-146`, assigned `:162`), `SlaUptimePercent` (`:340`, assigned `:401`), `LatencyTrend24h` (`:355-357`), `TopErrors` (`:345-346`). But **the frontend maps `latencyTrend24h` / `topErrors` / `errorRateTrend24h` and then discards them** — `monitoring.models.ts:341-343` populates them as `unknown[]` (`:141-143`) and **no component `.ts` and no `.html` under `src/frontend/src` references them** (only two `.spec.ts` fixtures do). `metricsStatus` is a **hardcoded constant** (`MonitoringStatus.RequiresObservabilityPipeline`) emitted unconditionally at `PlatformMonitoringService.cs:163`, `:285` and `:402` — no branch, no config read — so the UI reports "requires observability pipeline" beside real numbers. Two further real values are computed and dropped: per-tenant `p95` (`:352`, assigned to no DTO field) and `ErrorRateTrend24h` (hardcoded `Array.Empty<object>()` at `:398`). |
| AC-4 | **MET** | API calls, storage and email-send figures are all recorded and read back — see FR-24..FR-27. **Note the asymmetry (FR-24):** only API calls have a persisted aggregate table; storage and email are computed live at read time. The API-call slice shipped `b9906626` (2026-07-31); `docs/BA/STATUS.md` claimed for five weeks that it was deferred — corrected 2026-09-04. |
| AC-5 | **UNMET — no login-latency SLI exists** | `LoginCommandHandler.cs:33` is the handler's **entire** instrumentation: `HrmDomainMetrics.RecordLogin(result.IsSuccess)`, a `Counter<long>` tagged `outcome=success\|failure` and **nothing else — no duration** (`HrmDomainMetrics.cs:27-30, 45-46`). No `Stopwatch`, timestamp or histogram exists in the file; the only `Histogram<double>` in the class is `hrm.payroll.run.duration` (`:39-42`). **And** `/api/v1/auth` sits on the shared allow-list (`PlatformApiPaths.cs:24`), so `ApiCallCounterMiddleware` skips it (`:63`, `:91`) and login is **never bucketed into `tenant_latency_bucket`**. Login latency is measured by nothing, stored nowhere, and cannot be back-computed from existing data. **No SLO, error-budget, burn-rate or alerting construct of any kind exists in the repo.** |

**Net: 2 of 5 AC met, 1 partial, 2 unmet.**

## 4. Functional Requirements (IEEE 830 §3.2)

**OpenTelemetry — opt-in, dormant by default**
- FR-1: The API shall resolve a **three-way exporter mode** — `None` / `Otlp` / `Console`
  (`ObservabilityExtensions.cs:50`; resolver `:115-120`). `OpenTelemetry:Enabled` (nullable bool) is
  authoritative **in both directions** — both the opt-in and a hard kill-switch (`:71-78`); when unset,
  OTel is enabled only if an OTLP endpoint resolves.
- FR-2: The OTLP endpoint shall resolve in precedence order `OpenTelemetry:OtlpEndpoint` →
  `OTEL_EXPORTER_OTLP_ENDPOINT` (config key) → `OTEL_EXPORTER_OTLP_ENDPOINT` (raw env var); blank or
  whitespace resolves to null (`:54-63`).
- FR-3: **The shipped default shall be fully dormant.** `appsettings.json` ships
  `"OpenTelemetry": { "OtlpEndpoint": "" }` with **no `Enabled` key**, giving `ExporterMode.None`; in
  that mode `AddObservability` **returns before `services.AddOpenTelemetry()`** (`:164-166`; the
  registration sits at `:182`). No `TracerProvider` and no `MeterProvider` are registered — this is
  "not wired at all", not "wired with a no-op exporter". Nothing then listens for `Activity`, so
  ASP.NET Core creates none and per-request span cost is zero.
- FR-4: Sampling shall be `ParentBased(TraceIdRatioBased(ratio))` (`:193`) with ratio from
  `OpenTelemetry:SamplingRatio` (default `1.0`) **clamped to [0.0, 1.0]** (`:95-112`). The value is
  parsed from the raw string rather than `GetValue<double?>()` so malformed configuration cannot crash
  startup; a non-numeric or `NaN` value falls back to `1.0` (`:105-109`).
- FR-5: Trace instrumentation shall cover ASP.NET Core (`RecordException = true`), HttpClient, the
  `Npgsql` ActivitySource and `HRM.*`, plus Redis **conditionally** on a configured Redis connection
  (`:196-206`). Metrics shall cover ASP.NET Core, HttpClient, runtime and `HRM.*` (`:220-223`). Resource
  attributes: `service.name=HRM.Api`, `service.version`, `deployment.environment` (`:183-188`).
- FR-6: **EF Core OTel instrumentation is deliberately absent** — the package is not referenced because
  it remains pre-release (`HRM.Api.csproj:45-56`). Database spans come solely from Npgsql's built-in
  `ActivitySource`. A recorded decision, not an oversight.
- FR-7: A **Serilog→OTLP sink** shall be registered under a **stricter gate than the SDK itself**:
  `IsEnabled(config) && ResolveOtlpEndpoint(config) is not null` (`:129-130`). Console-exporter mode
  therefore enables traces/metrics but **not** the log sink — shipping logs at an endpoint-less
  `localhost:4317` would be worse than not shipping them (`:122-128`). When on: gRPC, resource
  `service.name=HRM.Api` (`:139-155`), additive alongside the existing Console, File and GlitchTip sinks
  (`Program.cs:58, 63`).
- FR-8: While OTel is **off**, an `ActivityListener` shall keep W3C trace ids alive
  (`LogCorrelationEnrichers.cs:128-143`, invoked `Program.cs:92`). It listens to every `ActivitySource`
  and samples `ActivitySamplingResult.PropagationData` on both callbacks — the cheapest level that still
  yields a trace id (including one continued from an inbound `traceparent`) while recording **no tags
  and no events**. It is registered **only** when OTel is disabled, because forcing always-sample would
  override OTel's own sampler.
- FR-9: Serilog enrichers shall add `trace_id`, `span_id` (`LogCorrelationEnrichers.cs:57-60`),
  `user_id` and `impersonated_by` (`:91-101`), registered `Program.cs:53-55`. `TraceContextEnricher`
  emits nothing when `Activity.Current` is null, deliberately avoiding an all-zero trace id (`:51-55`).
  **`tenant_id` is NOT enriched onto log records by any enricher** — a gap against tech-doc §37.1,
  recorded here rather than asserted as done.
- FR-10: Tenant span tags `tenant.id` / `tenant.subdomain` shall be set null-safely
  (`TenantResolutionMiddleware.cs:164-165`). Note the dot-separated names; the Sentry-side equivalent
  uses snake_case `tenant_id` (`TenantTagSentryEventProcessor.cs:29,31`). Under FR-8's dormant listener
  the Activity exists but samples `PropagationData`, so **these tags are not recorded on the shipped
  default** — they take effect only once OTel is enabled.

**Health probes**
- FR-11: `/health/live` shall map only checks tagged `live`, `/health/ready` only those tagged `ready`
  (`Program.cs:799-806`). A back-compat `/health` alias returns a static JSON 200 (`:809`).
- FR-12: Registered checks shall be exactly three — `self` (tag `live`), `postgres` (tag `ready`) and
  `redis` (tag `ready`, **conditionally registered only when a Redis connection string exists**)
  (`Program.cs:108-131`).
- FR-13: **The two readiness dependencies shall fail differently, by design.** Postgres uses the default
  `failureStatus` (`Unhealthy` → 503) and is a **hard** dependency. Redis is registered with
  `failureStatus: HealthStatus.Degraded` (`:129-130`) — the default status map returns **200** for
  `Degraded`, so a Redis outage surfaces on the probe **without** pulling the instance out of rotation,
  because the cache layer degrades gracefully to the database (rationale `:120-127`).
- FR-14: Probe responses shall use the framework-default `WriteMinimalPlaintext` — a plain-text body of
  `Healthy` / `Degraded` / `Unhealthy` only. **No `ResponseWriter` and no `ResultStatusCodes` override
  are configured**, so there is no JSON body and no per-check breakdown.
- FR-15: Both probes shall be **anonymous** — no `[Authorize]`, and `TenantResolutionMiddleware.cs:61-62`
  skips `/health`, so no JWT and no resolved tenant are required (`Program.cs:795-798`).
- FR-16: *NOT IMPLEMENTED* — there is **no Hangfire health check**; the three checks above are the whole
  set. Hangfire state is surfaced only through the monitoring DTO (`JobQueueSnapshotDto`,
  `PlatformMonitoringService.cs:117`), never through `/health/*`.
- FR-17: *NOT IMPLEMENTED* — tech-doc §37.4 also specifies `/health/tenant/{id}` (tenant-specific,
  internal). No such endpoint exists.

**SLA / uptime**
- FR-18: A Hangfire recurring job `health-probe-recorder` shall run **every 5 minutes** (`*/5 * * * *`,
  `Program.cs:1080-1083`) under explicit system context (`HealthProbeRecorderJob.cs:51`), executing the
  same **in-process** `ready` checks rather than a self-HTTP call (`:61-62`).
- FR-19: Each run shall write one `health_probe` row — `ObservedAtUtc`, `IsHealthy`, `Status`,
  `DurationMs` (`:75-82`). **`Degraded` counts as NOT healthy**: `IsHealthy = status == HealthStatus.Healthy`
  (`:79`). A throwing probe is recorded as `Unhealthy`, never dropped (`:65-71`).
- FR-20: Probe rows shall be pruned in the same run per `Monitoring:HealthProbeRetentionDays`, default
  **90** (`:29-31`, `:84-91`).
- FR-21: SLA uptime shall be computed over a **30-day** window (`PlatformMonitoringService.cs:551`;
  query `:521-534`), deliberately ≤ the default probe retention.
- FR-22: **With zero probes in the window the result shall be `null`, never 100** (`:541-547`) — with
  nothing measured, a perfect score is a fabrication and a zero is a false alarm.
- FR-23: `HealthProbe` shall be **platform-scope**: not a `BaseEntity`, no `TenantId`, no query filter,
  no RLS policy (`HealthProbe.cs:8-12`, `AppDbContext.cs:224`).

**Per-tenant usage + latency counters**
- FR-24: The three usage figures shall be recorded by **two different mechanisms, and this asymmetry is
  intentional**: API calls persist to an aggregate table (FR-25), whereas **storage and email have no
  table at all** and are computed live at read time — `TenantStorageUsage.ComputeBytesAsync`
  (`TenantStorageUsage.cs:21-33`) sums four live tables (`EmployeeDocuments`, `HrReportExports`,
  `PayrollReportExports`, `PayrollSlips`), and `TenantEmailSendUsage.CountSentThisMonthByTenantAsync`
  (`TenantEmailSendUsage.cs:32-47`) counts the `NotificationDelivery` ledger. Consequence: the API-call
  figure lags by up to one flush interval, while storage and email are exact-at-read.
- FR-25: API calls shall aggregate **per tenant per month** in `tenant_api_usage`
  (`YearMonth = year*100 + month`, `TenantApiUsage.cs:17-30`), unique on `(TenantId, YearMonth)`
  (`TenantApiUsageConfiguration.cs:26`), upserted DB-side with
  `ON CONFLICT (tenant_id, year_month) DO UPDATE SET call_count = call_count + EXCLUDED.call_count`
  (`TenantApiCallUsage.cs:59-65`). `CallCount` is `long` — a busy tenant can exceed `int` in a month.
- FR-26: Request latency shall aggregate **per tenant per hour per bucket** in `tenant_latency_bucket`,
  unique on `(TenantId, HourUtc, BucketIndex)` (`TenantLatencyBucketConfiguration.cs:27`), upserted with
  the same `+=` idiom (`TenantLatencyUsage.cs:25-31`). Buckets are **fixed** upper bounds
  `[5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000]` ms plus an implicit overflow bucket — 12 in
  total (`IApiCallCounter.cs:22-38`); bounds are fixed by design so hourly rows stay addable over time.
- FR-27: P95 shall be interpolated Prometheus-style within the containing bucket, returning the last
  finite bound as a floor when P95 lands in overflow, and **null (never 0) on an empty window**
  (`TenantLatencyUsage.cs:68-99`).
- FR-28: Counters shall accumulate in **lock-free in-memory buffers** —
  `ConcurrentDictionary<…, StrongBox<long>>` mutated via `Interlocked.Increment`
  (`ApiCallCounter.cs:19, 23, 28, 40`), drained read-and-zero-atomically with `Interlocked.Exchange`
  (`:48, 60`), registered **singleton** (`DependencyInjection.cs:813`).
- FR-29: A hosted service shall flush both buffers on **one shared 10-second tick**
  (`ApiCallCounterFlushService.cs:43-44`, `:96-98`; key `ApiCallCounter:FlushIntervalSeconds`, floor 1s,
  absent from `appsettings.json` so the 10s default ships), with kill switch `ApiCallCounter:Enabled`
  (`:48-52`) and a final flush on graceful shutdown (`:66`). An ungraceful shutdown loses the buffer.
- FR-30: The flush shall be **fail-safe**: on exception the deltas are re-buffered via `Interlocked.Add`
  and only a warning is logged (`ApiCallCounter.cs:68-75`, `ApiCallCounterFlushService.cs:104-110`).
- FR-31: The flush shall run inside `CrossTenantScope.Enter()` (`ApiCallCounterFlushService.cs:88`) so
  the cross-tenant upsert is not rejected by the RLS `WITH CHECK` policy. **Its absence previously caused
  839 consecutive silent metering failures** on the running stack (incident note `:81-87`).
- FR-32: Latency rows shall be pruned at **30 days** on the same tick
  (`ApiCallCounterFlushServiceConstants.Days = 30`, `:22`, applied `:102`).
- FR-33: Metering shall be measured in `ApiCallCounterMiddleware` around `_next` with
  `Stopwatch.GetTimestamp()` in a `finally` (`:43-51`), registered after routing/auth/tenant-status/
  entitlement and before controllers (`Program.cs:785`). Both counting paths are **fail-open** —
  exceptions swallowed and warn-logged (`:59-76`, `:80-101`).
- FR-34: A **single shared predicate** `PlatformApiPaths.IsMeteredTenantApiPath` shall decide both what
  is metered and what the US-ADM-012 entitlement gate inspects (`PlatformApiPaths.cs:8-11, 48-49`), so
  the two can never drift. Matching is **segment-aware** (`:55-57`): `/api/v1/auth` matches
  `/api/v1/auth/login` but not `/api/v1/authz-foo`.
- FR-35: The 13-entry allow-list (`PlatformApiPaths.cs:22-37`) shall exempt `/api/v1/auth`,
  `/api/v1/tenant/context`, `/tenant/settings`, `/tenant/users`, `/tenant/roles`, `/tenant/audit-logs`,
  `/tenant/workflows`, `/tenant/workflow-instances`, `/tenant/data-exports`, `/notifications`,
  `/notification-preferences`, `/notification-templates` and `/api/v1/system`. **State plainly: login is
  exempt from metering AND from latency bucketing** — this is what makes AC-5 unachievable from existing
  data.
- FR-36: Monitoring read endpoints shall be gated by the `Monitoring.View` **permission** — not a role
  check — on every action (`AdminMonitoringController.cs:28-30, 39, 57, 78-84`). They are GET-only.

**Not implemented (recorded so nobody assumes otherwise)**
- FR-37: *NOT IMPLEMENTED (AC-1)* — no telemetry **consumer** is deployed or defined anywhere: no OTel
  Collector, Tempo, Jaeger, Zipkin, Prometheus, Grafana or Loki, and no k8s/Helm manifests. Export is
  OTLP-**push**-only; there is no `/metrics` endpoint to scrape either.
- FR-38: *NOT IMPLEMENTED* — there is **no PII scrubbing or attribute filtering on the OTel span
  pipeline**. No `AddProcessor`, no `BaseProcessor<Activity>`, no `Filter`/`EnrichWithHttpRequest`
  exists. `SentryPiiScrubber` and `TenantTagSentryEventProcessor` operate on `SentryEvent` only and do
  **not** apply to spans. With `RecordException = true` (`ObservabilityExtensions.cs:196`), enabling OTel
  would ship exception messages and stack traces **unscrubbed**. See NFR-5 and §10.
- FR-39: *NOT IMPLEMENTED (AC-5)* — no login-latency SLI, and no SLO, error-budget, burn-rate, alert rule
  or threshold construct of any kind (tech-doc §37.5's alert table is entirely unimplemented).
- FR-40: *NOT IMPLEMENTED (AC-3)* — `metricsStatus` is a hardcoded constant at three sites
  (`PlatformMonitoringService.cs:163, 285, 402`); per-tenant P95 is computed and thrown away (`:352`);
  `ErrorRateTrend24h` is hardcoded empty (`:398`); and the FE discards `latencyTrend24h`/`topErrors`
  (`monitoring.models.ts:341-343`).

## 5. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1 (Performance — zero cost when off): With OTel dormant no provider is registered, so no `Activity`
  is created per request and observability overhead is **nil**, not merely small (FR-3). The dormant
  `ActivityListener` samples `PropagationData` only, allocating an id without recording tags/events.
- NFR-2 (Performance — metering off the hot path): Request handling shall never block on a database
  write. Counters accumulate in lock-free memory and flush asynchronously every 10s; the database does
  the addition via `ON CONFLICT … +=`, so concurrent flushes cannot lose increments (FR-25/26/28/29).
- NFR-3 (Availability — fail-open telemetry): Metering, latency recording and the flush loop are all
  fail-open; a telemetry fault degrades observability, never request handling (FR-30/33). This is the
  **opposite** posture to the US-ADM-012 plan-limit gates, which fail closed — see BR-6.
- NFR-4 (Availability — probe semantics): Readiness shall distinguish a hard dependency from a soft one.
  Postgres down ⇒ 503 and out of rotation; Redis down ⇒ 200 `Degraded` and still serving (FR-13).
- NFR-5 (Security / Privacy): Log records shall never carry secrets; enrichment is limited to
  `trace_id`, `span_id`, `user_id`, `impersonated_by` (FR-9). **Known risk, not a control:** the OTel
  span pipeline has **no** PII scrubbing and `RecordException = true`, so enabling OTel against an
  external collector would export unscrubbed exception detail (FR-38). This must be resolved **before**
  any collector is stood up.
- NFR-6 (Honesty of measurement): Every derived metric shall return **null on absent data rather than a
  fabricated value** — SLA uptime null on zero probes (FR-22), P95 null on an empty window (FR-27). A
  displayed 100% that nobody measured is worse than a blank.
- NFR-7 (Multi-tenancy): `tenant_api_usage` and `tenant_latency_bucket` shall carry EF global query
  filters (`AppDbContext.cs:799-800`, `:815-816`) **and** Postgres RLS policies (migrations
  `20260731012730_Platform_TenantApiUsage.cs:45-62`,
  `20260804004010_Monitoring_TenantLatencyHistogram.cs:58-60`). Because every monitoring read is
  deliberately cross-tenant and calls `IgnoreQueryFilters()`, effective isolation on these tables rests
  on **RLS + the `Monitoring.View` permission gate**; the EF filter is defence for the next reader.
- NFR-8 (Retention): Probe history 90 days (FR-20); latency buckets 30 days (FR-32); API-usage rows are
  monthly aggregates and are not pruned. The SLA window (30d) is deliberately ≤ probe retention (90d).
- NFR-9 (Operability): Observability shall be switchable without a code change —
  `OpenTelemetry:Enabled`, `OpenTelemetry:OtlpEndpoint`, `OpenTelemetry:SamplingRatio`,
  `ApiCallCounter:Enabled`, `ApiCallCounter:FlushIntervalSeconds`, `Monitoring:HealthProbeRetentionDays`.

## 6. Business Rules
- BR-1: **Observability is opt-in and inert by default.** A deployment that configures nothing gets no
  tracing, no metrics and no external network calls — but still gets trace ids in its logs (FR-3/FR-8).
- BR-2: **A metric that was not measured is reported as unavailable, never as a default.** Zero probes ⇒
  `null` uptime; empty window ⇒ `null` P95 (FR-22/FR-27).
- BR-3: **`Degraded` is not healthy for SLA purposes but IS healthy for load-balancer purposes.** The
  same status deliberately means different things to the probe consumer and to the uptime calculation
  (FR-13 vs FR-19).
- BR-4: **Redis is a soft dependency; Postgres is hard.** The cache degrades to the database, so a Redis
  outage must not remove an instance from rotation (FR-13).
- BR-5: **Platform/admin and auth traffic is neither metered nor billed.** The allow-list exempts
  `/api/v1/auth` and `/api/v1/system` among others, so a tenant is not charged API calls for logging in,
  nor for the platform operator inspecting them (FR-35).
- BR-6: **Telemetry fails open; entitlement and plan limits fail closed.** These two cross-cutting layers
  have deliberately opposite failure directions — losing a metric must never deny a request, whereas an
  unresolvable plan must never grant one (NFR-3; US-ADM-012 BR-2).
- BR-7: **The metering predicate and the entitlement predicate are the same function.** Any path added to
  the allow-list simultaneously stops being metered and stops being module-gated; the two cannot be
  changed independently (FR-34).
- BR-8: **Health-probe history is platform-scope, not tenant-scope.** It has no `TenantId` and is not
  tenant-isolable; per-tenant SLA reporting would need a different data source (FR-23).
- BR-9: **Only `Sent` email consumes the monthly figure.** Queued, Failed, Suppressed and Deferred
  deliveries do not count, and in-app notifications never do (`TenantEmailSendUsage.cs:13-19, 38-41`).

## 7. Data Requirements
- **Config (input):** `OpenTelemetry:Enabled` (bool?, unset), `OpenTelemetry:OtlpEndpoint` (string, `""`),
  `OpenTelemetry:SamplingRatio` (double, `1.0`), `OTEL_EXPORTER_OTLP_ENDPOINT` (env),
  `ApiCallCounter:Enabled` (bool, `true`), `ApiCallCounter:FlushIntervalSeconds` (int, `10`, absent from
  config), `Monitoring:HealthProbeRetentionDays` (int, `90`), `ConnectionStrings:Redis` (optional — its
  absence removes the Redis health check entirely).
- **Tables written:**
  - `health_probe` — `ObservedAtUtc`, `IsHealthy`, `Status`, `DurationMs`. **No `TenantId`.** 90-day retention.
  - `tenant_api_usage` — unique `(TenantId, YearMonth)`, `CallCount` (`long`). Monthly grain. RLS policy present.
  - `tenant_latency_bucket` — unique `(TenantId, HourUtc, BucketIndex)`, `Count`. Hourly × 12 buckets.
    30-day retention. RLS policy present.
- **Derived at read time (no table):** storage bytes (sum of `EmployeeDocuments`, `HrReportExports`,
  `PayrollReportExports`, `PayrollSlips`); email sends this month (`NotificationDelivery` where
  `Channel == Email && Status == Sent && SentAt >= monthStart`).
- **Emitted telemetry:** spans with `service.name` / `service.version` / `deployment.environment`, plus
  `tenant.id` / `tenant.subdomain` when OTel is enabled; meters `HRM.*` incl. `hrm.auth.login` (counter,
  `outcome` tag) and `hrm.payroll.run.duration` (histogram).
- **Log properties:** `trace_id`, `span_id`, `user_id`, `impersonated_by`. **Not** `tenant_id` (FR-9).
- **Read surface:** `GET /api/v1/system/monitoring/health`, `…/tenant-usage`, `…/tenants/{tenantId}` —
  all `Monitoring.View`, all GET.
- **Not stored anywhere:** login latency; any SLO/error-budget state; per-tenant P95 (computed, discarded).

## 8. UI/UX Notes
- The operator surface is the existing US-ADM-002 monitoring console; this story adds no new screen.
- `slaUptimePercent` is the one value rendered from the deferred block
  (`tenant-monitoring-detail.component.html:156-157`). A `null` uptime must render as "not available",
  never as `0%` or `100%` (BR-2).
- **Known UI defect (AC-3, not a design intent):** `latencyTrend24h`, `topErrors` and `errorRateTrend24h`
  are deserialized into `unknown[]` and never rendered — no chart, `@for` or table binds to them. Future
  work here is *rendering data that already arrives*, not fetching new data.
- **Known UI defect (AC-3):** the console shows the "requires observability pipeline" state
  unconditionally because `metricsStatus` is hardcoded, contradicting the real numbers displayed beside
  it. The fix is a **backend** change (derive the status), not a frontend one.
- Health endpoints are machine-facing plain text and have no UI.

## 9. Dependencies
- **US-ADM-002** — consumes these metrics; the AC-3 gap is visible there, not here.
- **US-ADM-012** — depends on `tenant_api_usage` for its `ApiCalls` gauge and shares the
  `PlatformApiPaths` predicate (BR-7). Note US-ADM-012 AC-3 is itself partly unmet *because*
  `max_api_calls_per_month` has a counter but no enforcement point.
- **US-PLT-006** — GlitchTip is **complementary, not a substitute**: it supplies `TopErrors` and the
  error-rate numerator (`PlatformMonitoringService.cs:130-137`), so the error-rate KPI is null whenever
  GlitchTip is unconfigured.
- **US-PLT-002** — the RLS layer the usage tables' isolation actually rests on (NFR-7).
- **US-NTF-006** — the `NotificationDelivery` ledger that the email figure is derived from.
- Hangfire (recurring probe), PostgreSQL (all three tables), Redis (optional soft health check).

## 10. Assumptions & Constraints
- **Assumption:** an OTLP-capable collector will be stood up *later*; until then the exporter
  configuration is exercised only by unit tests. **This is the AC-1 gap, not a plan** — no collector is
  scheduled, specified or scaffolded in this repo.
- **Constraint (blocking AC-1):** FR-38 — the span pipeline has no PII scrubbing and records exceptions.
  Standing up a collector **before** adding span-level redaction would export regulated HR PII outside
  the trust boundary, contradicting ADR-2026-07-08. Treat span scrubbing as a precondition of AC-1, not
  a follow-up.
- **Constraint:** latency bucket bounds are fixed and cannot be re-tuned without invalidating the
  addability of historical hourly rows (FR-26).
- **Constraint:** metering granularity is monthly for API calls and hourly for latency; sub-hour analysis
  is not possible from stored data.
- **Constraint:** an ungraceful process termination loses up to 10 seconds of unflushed counters (FR-29)
  — accepted, because the alternative is a synchronous write on every request.
- **Assumption:** `Rls:Enabled` ships `true` and is overridden to `false` in Development/CI, so the usage
  tables' RLS policies are enforced in production and dormant locally. A `true` flag with a blank
  `ConnectionStrings:PrivilegedConnection` **fails startup by design**
  (`DependencyInjection.cs:60-69`). *(Any documentation calling these policies "dormant" without that
  environment qualifier is describing Development only.)*
- **Assumption:** `HealthProbe` remaining tenant-agnostic is acceptable for Phase 1; per-tenant SLA
  reporting (tech-doc §35.3 "SLA tier") is **not** derivable from it (BR-8).
- **Constraint:** tech-doc §37.1 asks for `tenant_id` on every log record; FR-9 does not deliver it.
- **Constraint:** tech-doc §37.4's `/health/tenant/{id}` is unimplemented (FR-17).
- **Constraint:** tech-doc §37.5's alerting table is entirely unimplemented (FR-39).

## 11. Test Hints
- **AC-2:** `GET /health/live` with the DB stopped → still `Healthy` 200 (liveness must not depend on the
  DB). `GET /health/ready` with Postgres stopped → `Unhealthy` 503; with **Redis** stopped → `Degraded`
  **200**, not 503. Assert the body is plain text, not JSON.
- **AC-2 negative:** remove the Redis connection string entirely → the redis check is not registered at
  all and readiness is `Healthy`, not `Degraded`.
- **FR-3 (dormancy):** boot with shipped defaults and assert **no** `TracerProvider`/`MeterProvider` is
  resolvable from DI — asserting "no spans exported" would pass even if a provider were registered.
- **FR-8:** with OTel off, emit a log inside a request and assert a **non-zero** `trace_id`; send an
  inbound `traceparent` and assert the id is continued, not regenerated.
- **FR-22 (the crux honesty arm):** truncate `health_probe` and assert `SlaUptimePercent` is **null** — a
  mutant returning `100` must fail. Insert 9 `Healthy` + 1 `Degraded` and assert `90.0` (proves
  `Degraded` counts as unhealthy, BR-3).
- **FR-25/26:** fire N requests for two tenants concurrently, wait one flush tick, assert each tenant's
  `call_count` is exactly its own N and neither leaked into the other's row.
- **FR-30/31:** make the upsert throw for one tick and assert the deltas survive and land on the next
  tick (no lost counts). Separately, remove `CrossTenantScope` and assert the RLS `WITH CHECK` rejection
  reappears — that is the 839-failure regression.
- **FR-24:** upload a document and assert the storage figure changes **immediately** (no flush wait),
  proving storage is read-time-derived rather than buffered like API calls.
- **AC-5 (must fail until built):** assert a login produces **no** row in `tenant_latency_bucket` and no
  duration instrument on `hrm.auth.login`. This documents the gap; it is not a passing test.
- **AC-1 (must fail until built):** assert no compose/ops file defines a collector. Keep it red.
- **AC-3:** assert `metricsStatus` varies with actual pipeline state — currently impossible, so this arm
  documents the hardcoding.
- **NFR-7:** as a non-system tenant, query `tenant_api_usage` **without** `IgnoreQueryFilters` and assert
  only own-tenant rows; then verify RLS blocks the same read on the `hrm_app` role.
