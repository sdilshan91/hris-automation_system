---
id: US-PLT-004
module: Platform / Cross-Cutting
priority: Should Have
persona: System Admin / Platform Operator
status: draft
created: 2026-07-06
sprint: backlog
acceptance_criteria_count: 5
---

# US-PLT-004: Observability & Platform NFRs (OTel, Health, Per-Tenant Usage, SLOs)  [EPIC STUB]

> **STUB** — goal + AC skeleton + dependencies only; full detail to be authored before build.
> **Reconciliation story (COMPLETION-PLAN Theme I).** There is no OTel/tracing/metrics store; monitoring
> error-rate/latency/SLA values are hard-coded null; there are no `/health/live|ready` endpoints; per-tenant
> usage counters are absent; perf SLOs are uninstrumented (ties to ISSUE-203 login p95 3.86s). This story
> builds the observability substrate that US-ADM-002 monitoring and US-ADM-012 governance depend on.

## 1. Description
**As a** System Admin / Platform Operator,
**I want** real observability — distributed tracing, metrics, health probes, and per-tenant usage counters —
**So that** platform monitoring reflects real system state instead of hard-coded nulls, and SLOs can be measured.

## 2. Preconditions
- Serilog structured logging with RequestId/TenantId already in place (baseline).

## 3. Acceptance Criteria (SKELETON — expand before build)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The API is running | A request is processed | An OpenTelemetry trace + metrics are emitted (request latency, error count) to a configured exporter/store. |
| AC-2 | An orchestrator/probe checks the service | It calls `/health/live` and `/health/ready` | Liveness and readiness (DB/Redis/dependencies) are reported accurately. |
| AC-3 | The System Admin opens monitoring (US-ADM-002) | The page loads | Real error-rate %, P95 latency, and SLA/uptime values are shown (no longer hard-coded null). |
| AC-4 | Usage accrues per tenant | Usage is queried | Per-tenant counters (API calls, storage, emails) are recorded and exposed (feeds US-ADM-012 enforcement). |
| AC-5 | An SLO is defined (e.g. login p95) | Traffic flows | The SLO is instrumented and measurable (ties to ISSUE-203). |

## 4–10. Requirements (TO AUTHOR)
- FR/BR/NFR/data/UI to be written: OTel SDK wiring + exporter, metrics registry, health-check registrations, per-tenant metric dimensions, SLO definitions, retention/PII considerations in traces.

## 9. Dependencies
- US-ADM-002 (consumes the metrics), US-ADM-012 (usage counters), US-NTF-006 (delivery metrics).

## 11. Test Hints
- `/health/live|ready` respond correctly; a request produces a trace/metric; monitoring KPIs read real values; per-tenant counters increment.
