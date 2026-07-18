/**
 * US-ADM-002: System Admin Console — platform monitoring models.
 *
 * These DTOs match the System Admin monitoring backend contract EXACTLY
 * (AdminMonitoringController + MonitoringDtos.cs), rooted at
 * `/api/v1/system/monitoring/...` (the platform/system context).
 *
 * SOURCE OF TRUTH: the backend. Field names AND enum values mirror the C# DTOs
 * verbatim — enum values arrive as PascalCase strings ("Healthy", "Green",
 * "NotConfigured", "RequiresObservabilityPipeline"), so the FE unions are
 * PascalCase too. Do NOT lowercase them.
 *
 * HONEST RENDERING: the backend returns REAL data for some metrics and explicit
 * "RequiresObservabilityPipeline" markers + null values for others — no
 * OpenTelemetry / per-tenant usage-counter infrastructure exists yet. Nullable
 * fields and the `*Status === 'RequiresObservabilityPipeline'` flags indicate
 * metrics that require an observability pipeline that is not wired up. The UI
 * renders an em-dash / "Not available" placeholder, never a fabricated 0.
 */

/** Status marker the BE returns for deferred (not-yet-instrumented) metrics. */
export const REQUIRES_OBSERVABILITY = 'RequiresObservabilityPipeline';

/** Overall platform health roll-up (PlatformHealthStatus enum, serialized as a string). */
export type PlatformHealthStatus = 'Healthy' | 'Degraded' | 'Down';

/** Per-dependency probe health (DependencyHealthStatus enum). Redis may be NotConfigured. */
export type DependencyHealthStatus = 'Healthy' | 'Down' | 'NotConfigured';

/** Quota band for a usage gauge (UsageBand enum). */
export type UsageBand = 'Green' | 'Amber' | 'Red' | 'Breached';

/**
 * Hangfire cross-tenant job-queue snapshot (JobQueueSnapshotDto, FR-5).
 * When Hangfire storage is unavailable `available` is false and the counts are null.
 */
export interface IJobQueueSnapshot {
  available: boolean;
  enqueued: number | null;
  processing: number | null;
  scheduled: number | null;
  succeeded: number | null;
  failed: number | null;
}

/** Per-status tenant count for the health summary (TenantStatusCountDto). */
export interface ITenantStatusCount {
  status: string;
  count: number;
}

/**
 * GET /api/v1/system/monitoring/health — platform health summary (PlatformHealthDto, AC-1).
 *
 * The aggregate counts, DB/Redis/JobQueue signals, and the overall roll-up are REAL.
 * `aggregateErrorRatePercent` and `p95LatencyMs` are DEFERRED — null, with
 * `metricsStatus === 'RequiresObservabilityPipeline'`.
 */
export interface IPlatformHealth {
  overallStatus: PlatformHealthStatus;
  activeTenantCount: number;
  totalActiveUsers: number;
  tenantsByStatus: ITenantStatusCount[];
  databaseHealth: DependencyHealthStatus;
  redisHealth: DependencyHealthStatus;
  jobQueue: IJobQueueSnapshot;
  aggregateErrorRatePercent: number | null;
  p95LatencyMs: number | null;
  metricsStatus: string;
  generatedAtUtc: string;
}

/**
 * A single per-tenant usage gauge (UsageGaugeDto, AC-2). The EMPLOYEE gauge is REAL;
 * Storage / ApiCalls / EmailSends gauges are DEFERRED with `available === false` and
 * null numeric fields.
 */
export interface IUsageGauge {
  resource: string;
  available: boolean;
  used: number | null;
  limit: number | null;
  usagePercent: number | null;
  band: UsageBand | null;
}

/**
 * Per-tenant usage summary row (TenantUsageSummaryDto, AC-2/FR-2). The headline
 * employee gauge drives `usagePercent` / `band`; deferred gauges ride along in
 * `gauges[]` flagged `available === false`.
 */
export interface ITenantUsageSummary {
  tenantId: string;
  name: string;
  subdomain: string;
  status: string;
  plan: string;
  activeEmployees: number;
  employeeLimit: number | null;
  usagePercent: number | null;
  band: UsageBand | null;
  limitKnown: boolean;
  gauges: IUsageGauge[];
}

/**
 * GET /api/v1/system/monitoring/tenant-usage — the dashboard payload
 * (TenantUsageDashboardDto, AC-2/AC-3/FR-3). NOTE this is an OBJECT wrapper, not a
 * bare array: read `tenants` for the table. `attentionRequiredQueue` (error-rate)
 * is DEFERRED — empty, with `attentionQueueStatus === 'RequiresObservabilityPipeline'`.
 */
export interface ITenantUsageDashboard {
  tenants: ITenantUsageSummary[];
  quotaBreachQueue: ITenantUsageSummary[];
  attentionRequiredQueue: ITenantUsageSummary[];
  attentionQueueStatus: string;
  generatedAtUtc: string;
}

/**
 * GET /api/v1/system/monitoring/tenants/{id} — tenant monitoring detail
 * (TenantMonitoringDetailDto, AC-4).
 *
 * Status / plan / owner-email / created / last-activity / job status / employee
 * gauge are REAL. The 24h trend series, top errors, and SLA uptime are DEFERRED —
 * empty/null with `metricsStatus === 'RequiresObservabilityPipeline'`.
 */
export interface ITenantMonitoringDetail {
  tenantId: string;
  name: string;
  subdomain: string;
  status: string;
  plan: string;
  ownerEmail: string | null;
  createdAt: string;
  lastActivityAt: string | null;
  employeeUsage: IUsageGauge;
  jobQueue: IJobQueueSnapshot;
  errorRateTrend24h: unknown[];
  latencyTrend24h: unknown[];
  topErrors: unknown[];
  slaUptimePercent: number | null;
  metricsStatus: string;
  generatedAtUtc: string;
}

/** Filters for GET /tenant-usage (TenantUsageFilter, FR-4). Region/errorRate are no-ops. */
export interface ITenantUsageFilters {
  search?: string;
  status?: string;
  plan?: string;
  region?: string;
  createdFrom?: string;
  createdTo?: string;
}

/** Default polling interval for dashboard auto-refresh (ms) — AC-1 (configurable). */
export const DEFAULT_REFRESH_INTERVAL_MS = 30_000;

/** True when a metricsStatus / queue-status flag marks a field as not-yet-instrumented. */
export function requiresObservability(status: string | null | undefined): boolean {
  return status === REQUIRES_OBSERVABILITY;
}

/**
 * Tailwind classes for a usage band's gauge fill.
 * Green (0-79) / Amber (80-94) / Red (95-99) / Breached (>=100). Null → neutral.
 */
export function bandClass(band: UsageBand | null | undefined): string {
  switch (band) {
    case 'Green':
      return 'bg-green-500';
    case 'Amber':
      return 'bg-amber-500';
    case 'Red':
      return 'bg-red-500';
    case 'Breached':
      return 'bg-red-600';
    default:
      return 'bg-neutral-300';
  }
}

/**
 * Derive a usage band purely from a percentage, mirroring the BE
 * MonitoringClassifiers.ClassifyBand rule: Green 0-79, Amber 80-94, Red 95-99,
 * Breached >=100. The BE is the source of truth for `band`; this lets the FE
 * validate / colour-code consistently.
 */
export function bandFromPercent(percent: number | null): UsageBand | null {
  if (percent === null || percent === undefined) {
    return null;
  }
  if (percent >= 100) return 'Breached';
  if (percent >= 95) return 'Red';
  if (percent >= 80) return 'Amber';
  return 'Green';
}

/**
 * Accessible name for an employee usage gauge (BUG-105 / WCAG 2.1 AA 4.1.2). A
 * bare `role="progressbar"` announces only "progressbar"; this composes the metric,
 * the used/limit counts (∞ for an unlimited/null limit) and the percentage so a
 * screen reader announces the value. Optionally prefixed with the tenant name for
 * the multi-row dashboard table. Shared by the dashboard + detail gauges.
 */
export function employeeGaugeLabel(
  used: number | null | undefined,
  limit: number | null | undefined,
  usagePercent: number | null | undefined,
  tenantName?: string | null,
): string {
  const prefix = tenantName ? `${tenantName} — ` : '';
  const limitText = limit ?? '∞';
  const pctText =
    usagePercent !== null && usagePercent !== undefined
      ? ` (${Math.round(usagePercent)}%)`
      : '';
  return `${prefix}Employees: ${used ?? 0} of ${limitText}${pctText}`;
}

/**
 * True when a tenant row is at/above the 80% attention threshold (FR-3) — either
 * its headline band is Amber/Red/Breached, or its computed usagePercent is >= 80.
 */
export function needsAttention(tenant: ITenantUsageSummary): boolean {
  if (tenant.band === 'Amber' || tenant.band === 'Red' || tenant.band === 'Breached') {
    return true;
  }
  return tenant.usagePercent !== null && tenant.usagePercent >= 80;
}
