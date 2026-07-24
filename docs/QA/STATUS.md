# QA — STATUS

> Snapshot dashboard. Authoritative per-story/per-TC state lives in [`TEST-STATUS.md`](TEST-STATUS.md);
> defects in [`TEST-FINDINGS.md`](TEST-FINDINGS.md); bug rollup in [`BUG-STATUS.md`](BUG-STATUS.md).

- **Active plan:** [`plans/COMPLETION-PLAN.md`](plans/COMPLETION-PLAN.md) — P0 ledger reconcile + author missing TC suites (Training&Benefits / US-ADM-011 / US-NTF-006) → P1 (14 HIGH) → P2 (78 MED) → …
- **Execution model:** report-only loop (`/test-all`, `/test-us`) — never fixes, never opens PRs. See [INSTRUCTIONS](INSTRUCTIONS.md).
- **Open defects:** triage from [`TEST-FINDINGS.md`](TEST-FINDINGS.md); open HIGH/MED indexed in [`BLOCKERS.md`](BLOCKERS.md).
- **Monitoring / error-tracking (net-new 2026-07-24):** test approach for US-PLT-004 (OTel) + US-PLT-006 (GlitchTip) lives in the [observability plan §5.4](../Architecture/observability-otel-grafana-plan.md) — verify tenant-tagged errors reach GlitchTip with PII scrubbed, inert-when-unconfigured, no `tenant_id` Prometheus label. Report-only.

_Update when the active plan rolls over or a campaign closes; keep detail in the ledgers._
