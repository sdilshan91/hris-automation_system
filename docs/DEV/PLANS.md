# DEV — PLANS

- **Tooling adoption** — [`TOOLING-ADOPTION-PLAN.md`](TOOLING-ADOPTION-PLAN.md).
- **Observability & error tracking** (OTel/Grafana LGTM + self-hosted GlitchTip) — [`../Architecture/observability-otel-grafana-plan.md`](../Architecture/observability-otel-grafana-plan.md); feasibility rationale in [`../Architecture/advisory-reports/error-monitoring-feasibility.md`](../Architecture/advisory-reports/error-monitoring-feasibility.md). **Recommendation:** **GlitchTip first** (Phase 5), keep **Serilog** as-is, **defer the LGTM backend** until a perf question justifies it; **Datadog rejected** (tenant-PII cloud egress vs the governance ADR).
- **Frontend template/style extraction + SCSS standardization** (📋 PLANNED, deferred) — [`TEMPLATE-EXTRACTION-PLAN.md`](TEMPLATE-EXTRACTION-PLAN.md).
- **CI hardening** (incl. the RLS postgres-service-container job) is tracked with the architecture RLS work — [`../Architecture/PLANS.md`](../Architecture/PLANS.md).
