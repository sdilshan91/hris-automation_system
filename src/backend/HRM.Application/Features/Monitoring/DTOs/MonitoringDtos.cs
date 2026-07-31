namespace HRM.Application.Features.Monitoring.DTOs;

// ============================================================================
// US-ADM-002: System Admin monitors platform health and tenant usage.
//
// IMPORTANT — REAL vs DEFERRED data. This platform does NOT (yet) run an
// observability pipeline (OpenTelemetry metrics, per-tenant Redis usage
// counters, SLA probe history, a metrics store). The story assumes one. Rather
// than fabricate numbers, every field that has NO real backing data source is
// represented honestly: it is null and carries an explicit status/Available
// flag of "RequiresObservabilityPipeline". Only fields computed from data that
// actually exists in this codebase (the database + Hangfire job storage + a DB
// connectivity probe) carry real values. Each deferred field is documented at
// its declaration so it is obvious it is DEFERRED, not BROKEN.
// ============================================================================

/// <summary>Overall platform health roll-up (AC-1). A pure roll-up of the real signals below.</summary>
public enum PlatformHealthStatus
{
    /// <summary>All real signals nominal.</summary>
    Healthy = 0,
    /// <summary>A degraded-but-serving signal (Redis not configured/down, or Hangfire failed-job count high).</summary>
    Degraded = 1,
    /// <summary>A hard-down signal — the database is unreachable.</summary>
    Down = 2,
}

/// <summary>Status of an individual infrastructure dependency probe (AC-1 / FR-6).</summary>
public enum DependencyHealthStatus
{
    Healthy = 0,
    Down = 1,
    /// <summary>The dependency is not wired up in this deployment (e.g. Redis is configured-but-not-registered).</summary>
    NotConfigured = 2,
}

/// <summary>
/// Status markers for metrics the story asks for but that this platform cannot yet source from real data —
/// there is no metrics/observability store. Surfaced explicitly so the FE renders "not yet instrumented",
/// never a fake 0.
/// </summary>
public static class MonitoringStatus
{
    /// <summary>The metric/queue/series requires an observability pipeline this platform does not yet run.</summary>
    public const string RequiresObservabilityPipeline = "RequiresObservabilityPipeline";
}

/// <summary>
/// Hangfire cross-tenant job queue snapshot (FR-5). REAL — sourced from Hangfire's monitoring API
/// (<c>JobStorage.Current.GetMonitoringApi()</c>) when Hangfire storage is available. When it is not
/// (tests / dev without a Hangfire server) <see cref="Available"/> is false and the counts are null.
/// </summary>
public sealed record JobQueueSnapshotDto(
    bool Available,
    long? Enqueued,
    long? Processing,
    long? Scheduled,
    long? Succeeded,
    long? Failed);

/// <summary>Per-status tenant count for the platform health summary (REAL — grouped from the tenants table).</summary>
public sealed record TenantStatusCountDto(string Status, int Count);

/// <summary>
/// Platform health summary (AC-1 / FR-1 / FR-6). The aggregate counts, DB/Redis/Hangfire signals, and the
/// overall roll-up are REAL. The error-rate % and P95 latency are DEFERRED (no metrics store) — they are null
/// and <see cref="MetricsStatus"/> is "RequiresObservabilityPipeline".
/// </summary>
public sealed record PlatformHealthDto(
    // ── Real roll-up ────────────────────────────────────────────────────────
    PlatformHealthStatus OverallStatus,
    int ActiveTenantCount,
    int TotalActiveUsers,
    IReadOnlyList<TenantStatusCountDto> TenantsByStatus,
    DependencyHealthStatus DatabaseHealth,
    DependencyHealthStatus RedisHealth,
    JobQueueSnapshotDto JobQueue,

    // ── Deferred (no observability pipeline) ─────────────────────────────────

    /// <summary>DEFERRED: aggregate error-rate % over the last 5 min. No metrics store — always null.</summary>
    double? AggregateErrorRatePercent,
    /// <summary>DEFERRED: P95 request latency (ms). No metrics store — always null.</summary>
    double? P95LatencyMs,
    /// <summary>"RequiresObservabilityPipeline" — marks the two deferred metrics above as not-yet-instrumented.</summary>
    string MetricsStatus,

    DateTime GeneratedAtUtc);

/// <summary>
/// Usage-band classification of a gauge (AC-2). Boundaries: green 0-79, amber 80-94, red 95-100,
/// breached &gt;=100 (FR-3). Computed by the pure <c>UsageBandClassifier</c> so it is unit-testable.
/// </summary>
public enum UsageBand
{
    Green = 0,
    Amber = 1,
    Red = 2,
    Breached = 3,
}

/// <summary>
/// A single per-tenant usage gauge (AC-2 / US-ADM-012 AC-4 / US-PLT-004). All four gauges are now REAL:
/// Employees = active employees vs plan limit; Storage = cumulative stored bytes (all four size-bearing tables,
/// in MB) vs <c>max_storage_gb</c>; EmailSends = month-to-date successful sends vs <c>max_email_sends_per_month</c>;
/// ApiCalls = month-to-date persisted API calls (off the <c>tenant_api_usage</c> aggregate) vs
/// <c>max_api_calls_per_month</c>.
/// A null <see cref="Limit"/> with <see cref="Available"/> true means UNLIMITED (BR-3): <see cref="UsagePercent"/>
/// and <see cref="Band"/> are null (no cap to divide against), while <see cref="Used"/> still reports real usage.
/// </summary>
public sealed record UsageGaugeDto(
    string Resource,
    bool Available,
    int? Used,
    int? Limit,
    double? UsagePercent,
    UsageBand? Band);

/// <summary>
/// Per-tenant usage summary row for the System Admin dashboard table (AC-2 / FR-2). The employee gauge drives
/// the headline <see cref="UsagePercent"/> / <see cref="Band"/>; the deferred gauges ride along in
/// <see cref="Gauges"/> flagged Available=false.
/// </summary>
public sealed record TenantUsageSummaryDto(
    Guid TenantId,
    string Name,
    string Subdomain,
    string Status,
    string Plan,
    // Headline employee gauge (REAL)
    int ActiveEmployees,
    int? EmployeeLimit,
    double? UsagePercent,
    UsageBand? Band,
    bool LimitKnown,
    IReadOnlyList<UsageGaugeDto> Gauges);

/// <summary>
/// The tenant-usage dashboard payload (AC-2 / AC-3 / FR-3). Carries the full filtered tenant list plus the
/// quota-breach queue (tenants &gt;=80% of the employee limit, severity desc). The error-rate "Attention
/// Required" queue (AC-3) is DEFERRED — empty + a documented status flag.
/// </summary>
public sealed record TenantUsageDashboardDto(
    IReadOnlyList<TenantUsageSummaryDto> Tenants,
    IReadOnlyList<TenantUsageSummaryDto> QuotaBreachQueue,

    /// <summary>DEFERRED: tenants with an elevated error rate (AC-3). No metrics store — always empty.</summary>
    IReadOnlyList<TenantUsageSummaryDto> AttentionRequiredQueue,
    /// <summary>"RequiresObservabilityPipeline" — marks the error-rate queue above as not-yet-instrumented.</summary>
    string AttentionQueueStatus,

    DateTime GeneratedAtUtc);

/// <summary>
/// Filters for the tenant-usage dashboard (FR-4). Status / plan / search / created-date range are REAL.
/// Region and ErrorRateThreshold are accepted but DEFERRED no-ops (no region field on Tenant; no metrics).
/// </summary>
public sealed record TenantUsageFilter(
    string? Status = null,
    string? Plan = null,
    string? Search = null,
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedToUtc = null,
    /// <summary>DEFERRED no-op: no Region field exists on Tenant in this codebase.</summary>
    string? Region = null,
    /// <summary>DEFERRED no-op: no per-tenant error-rate metric exists.</summary>
    double? ErrorRateThreshold = null);

/// <summary>
/// Per-tenant operational detail view (AC-4). Status / plan / owner-email / created / last-activity / job
/// status / employee gauge are REAL. The 24h error-rate &amp; latency trend series, top-errors, and SLA uptime
/// are DEFERRED — empty/null with status flags. Owner email is operational, not PII (BR-2).
/// </summary>
public sealed record TenantMonitoringDetailDto(
    Guid TenantId,
    string Name,
    string Subdomain,
    string Status,
    string Plan,
    string? OwnerEmail,
    DateTime CreatedAt,
    DateTime? LastActivityAt,
    UsageGaugeDto EmployeeUsage,
    JobQueueSnapshotDto JobQueue,

    /// <summary>DEFERRED: 24h error-rate trend points. No metrics store — always empty.</summary>
    IReadOnlyList<object> ErrorRateTrend24h,
    /// <summary>DEFERRED: 24h latency trend points. No metrics store — always empty.</summary>
    IReadOnlyList<object> LatencyTrend24h,
    /// <summary>DEFERRED: grouped top errors. No metrics store — always empty.</summary>
    IReadOnlyList<object> TopErrors,
    /// <summary>DEFERRED: SLA uptime % from probe history (FR-7). No probe history — always null.</summary>
    double? SlaUptimePercent,
    /// <summary>"RequiresObservabilityPipeline" — marks the deferred trend/SLA fields above.</summary>
    string MetricsStatus,

    DateTime GeneratedAtUtc);
