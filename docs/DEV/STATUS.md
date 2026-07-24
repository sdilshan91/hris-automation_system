# DEV — STATUS

- **Stack:** Angular 20 (`src/frontend`) + ASP.NET Core 10 (`src/backend`) + PostgreSQL.
- **Backend tests:** xUnit + Testcontainers (Docker required). **Frontend:** Karma + Jasmine.
- **Local run:** native (PG18) or Docker stack — see [INSTRUCTIONS](INSTRUCTIONS.md) and [`../../local-dev/`](../../local-dev/).
- **CI:** [`../../.github/workflows/`](../../.github/workflows/).
- **Observability:** Serilog console+file **live**; OpenTelemetry instrumentation **coded but dormant** (endpoint-gated — blank `OtlpEndpoint` ⇒ Console exporter only, no backend); GlitchTip error-tracking **decided + scaffolded** (`ops/glitchtip/`), **not yet wired**. Plan: [`../Architecture/observability-otel-grafana-plan.md`](../Architecture/observability-otel-grafana-plan.md).
