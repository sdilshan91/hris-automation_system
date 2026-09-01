---
name: us-adm-002-monitoring-run
description: How US-ADM-002 platform-monitoring TCs map to REAL routes + which are blocked-by-design vs missing-seed; audit verification recipe
metadata:
  type: project
---

US-ADM-002 (System Admin monitors platform health/usage) — executed API-layer 2026-06-24. 7 PASS / 11 BLOCKED, findings ISSUE-002/003.

**Real routes** (TC text says `/api/v1/admin/monitoring/...` — WRONG): the surface is 3 GETs under
`/api/v1/system/monitoring/` → `health`, `tenant-usage` (FR-4 filters status/plan/search/createdFrom/createdTo work; region+errorRateThreshold are accepted DEFERRED no-ops), `tenants/{tenantId:guid}`. All gated by
`Monitoring.View` permission (held by SystemAdmin AND System Support — story is read-only so support gets full read; Tenant Admin lacks it → 403). No mutating monitoring endpoint exists.

**Honest-deferred design** (`PlatformMonitoringService` + `MonitoringDtos`): this platform has NO observability
pipeline, so error-rate%/P95/24h-trends/SLA-uptime/storage+API+email gauges/error-rate "Attention Required"
queue are returned null/empty with `MetricsStatus="RequiresObservabilityPipeline"` — never fabricated. Only
REAL: DB connectivity probe, tenant/user counts, Hangfire snapshot, and the EMPLOYEE usage gauge (vs
`Tenant.MaxEmployees` or plan `MaxEmployees`; null limit = unlimited = excluded from breach queue). Bands:
green 0-79 / amber 80-94 / red 95-99 / breached >=100; breach queue = >=80% sorted desc. TC-14..18 stay
`status: blocked` (their DEFERRED reason) — don't flip them.

**Why TC-02/03/04 are BLOCKED not PASS:** the 80/95/100% employee-breach boundaries need multi-tenant seeded
ratios (e.g. max_employees=5 with 4 then 5 employees) that are NOT seeded — every seeded tenant is at 0% or
null-limit, so breach queue is correctly empty. Fail-closed → blocked (no defect). Seeding those ratios is the
only way to run them green; report-only must not invent the data.

**Audit verification recipe (no psql on this box, no system-audit read API):** monitoring rows are
system-scoped (`tenant_id = NULL`, action `Monitoring.Viewed` / `Monitoring.TenantViewed`, `resource_id` =
viewed tenantId for detail) so the tenant audit-log endpoint can't see them. Verified directly against PG with
a throwaway `dotnet` console (Npgsql 9.0.2 PackageReference — referencing the API's Npgsql.dll alone fails:
missing `Microsoft.Extensions.Logging.Abstractions`), reading the conn string from HRM.Api user-secrets
(`dotnet user-secrets list`), querying `audit_logs`. `after` column is json → cast `after::text`. Confirmed
before/after +1/+1 on a fresh view, actor user_id populated, payload aggregates-only (no PII, BR-2).

See [[qa-baseline-2026-06-19]] (API-layer-via-admin method, acme personas) and [[testing-loop-report-only]].
