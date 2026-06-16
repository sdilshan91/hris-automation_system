/**
 * US-ADM-002: System Admin Console — platform monitoring models.
 *
 * These DTOs match the System Admin monitoring backend contract, rooted at
 * `/api/admin/monitoring/...` (the platform/system context), NOT the
 * tenant-scoped `/api/v1/...` namespace.
 *
 * HONEST RENDERING: the backend returns REAL data for some metrics and explicit
 * "not available" markers for others — no OpenTelemetry / SignalR / usage-counter
 * infrastructure exists yet. Nullable fields and `metricsAvailable === false`
 * indicate metrics that require an observability pipeline that is not wired up.
 */

/** Overall platform health rollup. */
export type PlatformHealthStatus = 'healthy' | 'degraded' | 'down';

/** Redis is optional infra — it may simply not be configured. */
export type RedisHealth = 'healthy' | 'down' | 'notConfigured';

/** Per-component health (DB / Redis probe). */
export type ComponentHealth = 'healthy' | 'degraded' | 'down' | 'notConfigured';

/** Quota band for a usage gauge. */
export type UsageBand = 'green' | 'amber' | 'red' | 'breached';

/** Hangfire background-job queue snapshot (FR-5). */
export interface IHangfireStatus {
  queued: number;
  processing: number;
  succeeded: number;
  failed: number;
}

/** Tenant counts grouped by lifecycle status (e.g. trial / active / suspended). */
export interface ITenantsByStatus {
  [status: string]: number;
}

/**
 * GET /api/admin/monitoring/health — platform health rollup (AC-1, FR-1, FR-6).
 *
 * `errorRate` and `p95LatencyMs` are nullable and gated by `metricsAvailable`:
 * when the observability pipeline is absent the BE returns `metricsAvailable:false`
 * and null metrics — the UI must render a "Not available" placeholder, never 0.
 */
export interface IPlatformHealth {
  status: PlatformHealthStatus;
  activeTenants: number;
  activeUsers: number;
  tenantsByStatus: ITenantsByStatus;
  dbHealth: ComponentHealth;
  redisHealth: RedisHealth;
  hangfire: IHangfireStatus;
  errorRate: number | null;
  p95LatencyMs: number | null;
  metricsAvailable: boolean;
}

/** A single usage dimension (employees / storage / API / email). */
export interface IUsageMetric {
  used: number;
  limit: number | null;
  percent: number | null;
  band: UsageBand;
}

/**
 * GET /api/admin/monitoring/tenants — per-tenant usage summary row (AC-2, FR-2/3).
 *
 * `storageUsage`, `apiUsage`, `emailUsage` are nullable: they depend on Redis
 * usage counters that are not yet incremented, so the BE returns null and the UI
 * shows an em-dash placeholder rather than a fake "0".
 */
export interface ITenantUsageSummary {
  tenantId: string;
  name: string;
  subdomain: string;
  status: string;
  plan: string;
  employeeUsage: IUsageMetric;
  storageUsage: IUsageMetric | null;
  apiUsage: IUsageMetric | null;
  emailUsage: IUsageMetric | null;
}

/** A point in an error-rate / latency trend series (24h). */
export interface ITrendPoint {
  timestamp: string;
  value: number;
}

/**
 * GET /api/admin/monitoring/tenants/{id} — tenant monitoring detail (AC-4).
 *
 * `errorTrend` / `latencyTrend` / `slaUptime` are nullable: they require the
 * observability pipeline and are returned as null until it exists.
 */
export interface ITenantMonitoringDetail {
  tenantId: string;
  name: string;
  subdomain: string;
  status: string;
  plan: string;
  ownerEmail: string;
  createdAt: string;
  lastActivityAt: string | null;
  employeeUsage: IUsageMetric;
  hangfire: IHangfireStatus;
  errorTrend: ITrendPoint[] | null;
  latencyTrend: ITrendPoint[] | null;
  slaUptime: number | null;
}

/** Filters for GET /api/admin/monitoring/tenants (FR-4). */
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

/**
 * Tailwind classes for a usage band's gauge fill + text.
 * green (0-79) / amber (80-94) / red (95-100) / breached (>=100).
 */
export function bandClass(band: UsageBand): string {
  switch (band) {
    case 'green':
      return 'bg-green-500';
    case 'amber':
      return 'bg-amber-500';
    case 'red':
      return 'bg-red-500';
    case 'breached':
      return 'bg-red-600';
    default:
      return 'bg-neutral-300';
  }
}

/**
 * Derive a usage band purely from a percentage. The BE is the source of truth
 * for `band`, but this mirrors its rule so the FE can validate/colour-code
 * consistently and the gauge spec can assert it.
 * green 0-79, amber 80-94, red 95-99, breached >=100.
 */
export function bandFromPercent(percent: number | null): UsageBand | null {
  if (percent === null || percent === undefined) {
    return null;
  }
  if (percent >= 100) return 'breached';
  if (percent >= 95) return 'red';
  if (percent >= 80) return 'amber';
  return 'green';
}

/** True when a usage metric is at or above the 80% attention threshold (FR-3). */
export function needsAttention(metric: IUsageMetric): boolean {
  if (metric.band === 'amber' || metric.band === 'red' || metric.band === 'breached') {
    return true;
  }
  return metric.percent !== null && metric.percent >= 80;
}
