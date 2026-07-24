# QA — PLANS

Plans now live in [`plans/`](plans/); closed/superseded dated plans in [`plans/archive/`](plans/archive/);
dated QA reports/snapshots in [`reports-archive/`](reports-archive/).

## The living completion plan
**[`plans/COMPLETION-PLAN.md`](plans/COMPLETION-PLAN.md)** — the ONE active plan. It rolls over **in place**
(dated **Changelog** at its top); do **not** create a new `COMPLETION-PLAN-<date>.md`. Full snapshots of
superseded versions are kept under [`plans/archive/`](plans/archive/).

## Active / living plans (`plans/`)
| Plan | Status |
|------|--------|
| [plans/COMPLETION-PLAN.md](plans/COMPLETION-PLAN.md) | ✅ **ACTIVE** (living) |
| [plans/QA-COVERAGE-PLAN.md](plans/QA-COVERAGE-PLAN.md) | living |
| [plans/TEST-ENV-SETUP-PLAN.md](plans/TEST-ENV-SETUP-PLAN.md) | living |
| [plans/INTEGRATION-PERF-TEST-PLAN.md](plans/INTEGRATION-PERF-TEST-PLAN.md) | reference (pairs [`../../perf/`](../../perf/)) |
| [plans/TEST-COVERAGE-PLAN-2026-06-23.md](plans/TEST-COVERAGE-PLAN-2026-06-23.md) | reference |
| [plans/TEST-AUTOMATION-PLAN-2026-06-19.md](plans/TEST-AUTOMATION-PLAN-2026-06-19.md) | reference |

## Archived / closed plans (`plans/archive/`)
| Plan | Note |
|------|------|
| [COMPLETION-PLAN-2026-07-10.md](plans/archive/COMPLETION-PLAN-2026-07-10.md) | superseded (folded into the living plan's changelog) |
| [COMPLETION-PLAN-2026-07-06.md](plans/archive/COMPLETION-PLAN-2026-07-06.md) | superseded |
| [FIX-FINDINGS-PLAN-2026-07-04.md](plans/archive/FIX-FINDINGS-PLAN-2026-07-04.md) | CLOSED |
| [BLOCKED-TC-REEXEC-PLAN-2026-07-03.md](plans/archive/BLOCKED-TC-REEXEC-PLAN-2026-07-03.md) | CLOSED |
| [BLOCKED-TC-REMEDIATION-PLAN-2026-07-02.md](plans/archive/BLOCKED-TC-REMEDIATION-PLAN-2026-07-02.md) | CLOSED |
| [BLOCKER-VERIFICATION-PLAN-2026-07-02.md](plans/archive/BLOCKER-VERIFICATION-PLAN-2026-07-02.md) | CLOSED |

## Dated reports / snapshots (`reports-archive/`)
[BUG-REPORT-2026-06-19](reports-archive/BUG-REPORT-2026-06-19.md) · [QA-COVERAGE-REPORT-2026-06-19](reports-archive/QA-COVERAGE-REPORT-2026-06-19.md) · [QA-STATUS-SHEET-2026-06-23](reports-archive/QA-STATUS-SHEET-2026-06-23.md) · [MEDLOW-TRIAGE-2026-07-05](reports-archive/MEDLOW-TRIAGE-2026-07-05.md) · [PRODUCT-DECISIONS-NEEDED-2026-07-05](reports-archive/PRODUCT-DECISIONS-NEEDED-2026-07-05.md)

## Monitoring / error-tracking test approach (net-new 2026-07-24)
How QA verifies the observability work — **US-PLT-004** (OTel) + **US-PLT-006** (GlitchTip) — is specified in the
[observability plan](../Architecture/observability-otel-grafana-plan.md) (§Phase 4 verification + §5.4). Report-only via
`@test-runner`: prove a thrown exception reaches GlitchTip **tagged by tenant with PII scrubbed** (request-body /
`Authorization` / email stripped), prove blank DSN / `OtlpEndpoint` ⇒ **inert** (nothing ships), and confirm **no
`tenant_id` leaks as a Prometheus label** (cardinality guard). Rationale: [error-monitoring-feasibility.md](../Architecture/advisory-reports/error-monitoring-feasibility.md).

> Plans are living documents (Engineering-Discipline rule #6 / `/auto-heal`) — the completion plan re-sorts as reality changes.
