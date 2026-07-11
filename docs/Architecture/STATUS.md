# Architecture — STATUS

- **Reference spec:** [`hrm_technical_document_v4.0.md`](hrm_technical_document_v4.0.md) (v4.0).
- **Tenant isolation:** shared-DB + row-discriminator + **Row-Level Security built & merged, flag OFF** (`Rls:Enabled=false`); flip re-validated GO. ADR in [`../vault/decisions/`](../vault/decisions/).
- **Observability:** OpenTelemetry (OTLP endpoint-gated) + `/health/live|ready`; plan in [`observability-otel-grafana-plan.md`](observability-otel-grafana-plan.md).
- **Tech-radar:** [`radar/`](radar/).
