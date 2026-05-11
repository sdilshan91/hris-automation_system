---
id: US-ADM-002
module: Admin Console — System Admin
priority: Must Have
persona: System Admin (Platform Operator Staff)
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ADM-002: System Admin Monitors Platform Health and Tenant Usage

## 1. Description
**As a** System Admin (Platform Operator Staff),
**I want to** view a comprehensive monitoring dashboard that shows platform-wide health metrics, per-tenant usage gauges, error rates, latency, and quota breach alerts,
**So that** I can proactively identify issues, ensure SLA compliance, and take corrective action before tenants are impacted.

## 2. Preconditions
- The System Admin is authenticated at `admin.yourhrm.com` with the `SystemAdmin` role.
- OpenTelemetry metrics collection is operational and exporting data.
- At least one tenant exists on the platform.
- Health check endpoints (`/health/live`, `/health/ready`, `/health/tenant/{id}`) are implemented.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The System Admin navigates to the Monitoring dashboard | The page loads | The dashboard displays: overall platform health status (healthy/degraded/down), aggregate error rate (percentage over last 5 min), P95 latency, active tenant count, total active users, DB health, Redis health, and Hangfire job queue depth. Data refreshes automatically via SignalR or polling (configurable interval, default 30s). |
| AC-2 | The System Admin views the per-tenant usage section | They search or filter by tenant name/subdomain | The system shows per-tenant gauges for: active employees vs plan limit, storage used vs plan limit (GB), API calls this month vs plan limit, email sends this month vs plan limit. Tenants exceeding 80% of any limit are highlighted with a warning indicator; tenants at 100% are flagged as breached. |
| AC-3 | A tenant's error rate exceeds 5% over 10 minutes | The System Admin views the dashboard | That tenant appears in the "Attention Required" queue with the elevated error rate, last error sample, and a link to the tenant's detail/debug view. |
| AC-4 | The System Admin clicks on a specific tenant in the dashboard | The tenant detail view opens | The view shows: tenant status, plan, owner, creation date, last activity, error rate trend (24h chart), latency trend (24h chart), top errors (grouped), background job status, and quick-action buttons (impersonate, suspend, view audit log). |
| AC-5 | The System Admin accesses monitoring data | The system processes the request | No tenant-specific PII (employee names, salaries, etc.) is exposed in the monitoring views. Only aggregate/operational metrics are shown. All monitoring access is logged to `system_audit_log`. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The monitoring dashboard shall display real-time platform health indicators sourced from OpenTelemetry metrics and health check endpoints.
- FR-2: Per-tenant usage gauges shall be computed from: `tenant` + `subscription_plan` tables for limits, and usage counters from Redis (`t:{tenantId}:usage:*`) or materialized views.
- FR-3: The quota breach queue shall list tenants that have exceeded 80%, 95%, or 100% of any plan limit, sorted by severity.
- FR-4: The tenant health dashboard shall support filtering by: status, plan, region, error rate threshold, and creation date range.
- FR-5: The dashboard shall show Hangfire background job status in a cross-tenant view: queued, processing, succeeded, failed counts, with the ability to drill into failed jobs filtered by tenant.
- FR-6: DB and Redis health shall be determined by the `/health/ready` endpoint and displayed as status indicators.
- FR-7: SLA tracking shall show uptime percentage per tenant (calculated from health check probes) against the tenant's plan SLA tier.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The monitoring dashboard shall load within 2.5 seconds (P95) including initial data fetch.
- NFR-2: Real-time updates shall be delivered via SignalR with a maximum delay of 5 seconds from metric collection to dashboard display.
- NFR-3: Monitoring queries shall not impact production database performance; use read replicas or pre-aggregated materialized views.
- NFR-4: The dashboard shall render correctly on screens from 1024px to 4K (admin console may assume minimum 1024px width).
- NFR-5: All dashboard access shall be audited to `system_audit_log` including which tenant details were viewed.

## 6. Business Rules
- BR-1: Only `SystemAdmin` and `SystemSupport` roles may access the monitoring dashboard. `SystemSupport` has read-only access (no quick-action buttons for suspend/terminate).
- BR-2: Monitoring data shall never expose tenant-level PII. Employee counts are shown, but employee names/details are not.
- BR-3: Tenant usage counters reset on the first day of each calendar month (aligned with plan billing cycle).
- BR-4: When a tenant reaches 95% of any plan limit, the system shall automatically send an in-app notification to the tenant admin and an alert to the System Admin dashboard.

## 7. Data Requirements
- **Input (filters):** tenant name/subdomain (string), status (enum), plan (UUID), date range, error rate threshold (percentage).
- **Output (dashboard):** Platform health object, array of tenant usage summaries, quota breach list, error trend time series, Hangfire job counts.
- **Data sources:** OpenTelemetry metrics, PostgreSQL (`tenant`, `subscription_plan`, `tenant_lifecycle_event`), Redis usage counters (`t:{tenantId}:usage:employees`, `t:{tenantId}:usage:storage_bytes`, etc.), health check endpoints, Hangfire storage.

## 8. UI/UX Notes
- Dashboard layout uses a card-based grid: top row for platform health KPIs (health status, error rate, P95 latency, active tenants, active users), second row for charts (error rate trend, latency trend), third row for the tenant usage table with inline gauges.
- Use Angular Material data table with sorting, pagination, and search for the tenant list.
- Gauge bars use color coding: green (0-79%), amber (80-94%), red (95-100%).
- Charts use a lightweight charting library (e.g., ngx-charts or Chart.js).
- Follow the Notion/Linear aesthetic: clean, minimal, with subtle shadows and smooth transitions.
- "Attention Required" queue uses a slide-out panel or dedicated tab with badge count.

## 9. Dependencies
- OpenTelemetry metrics pipeline must be configured and exporting.
- Health check endpoints must be implemented.
- Redis usage counters must be incremented by relevant application operations (employee creation, file upload, API calls, email sends).
- SignalR hub for real-time dashboard updates.
- Hangfire dashboard data (or API) must be accessible from the system admin context.

## 10. Assumptions & Constraints
- Phase 1 uses a single-region deployment; multi-region health aggregation is not required.
- The monitoring dashboard is for operational awareness; detailed APM (Application Performance Monitoring) is handled by external tools (e.g., Seq, App Insights).
- Historical metric retention in the application database is limited to 90 days; longer retention is handled by the external observability stack (ELK/Splunk).

## 11. Test Hints
- **Dashboard load:** Verify the dashboard loads within 2.5s with 100+ tenants and displays correct aggregate metrics.
- **Quota breach:** Provision a tenant with a plan having `max_employees = 5`, create 4 employees (expect 80% warning), create the 5th (expect 100% breach flag).
- **Tenant isolation in monitoring:** Verify that monitoring views show only aggregate data and never expose PII from any tenant.
- **Real-time update:** Trigger an error in a tenant's API call; verify the dashboard reflects the updated error rate within 5 seconds.
- **Access control:** Authenticate as a Tenant Admin and attempt to access `admin.yourhrm.com/monitoring`; verify access is denied (403).
- **Audit logging:** View a tenant's detail in monitoring; verify the action is recorded in `system_audit_log`.
