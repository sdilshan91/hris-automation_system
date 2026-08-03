using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Monitoring;
using HRM.Application.Features.Monitoring.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace HRM.Infrastructure.Services;

/// <summary>
/// System-admin platform monitoring (US-ADM-002). Runs in the system/admin context (no resolved tenant), so
/// every tenant-scoped query uses <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/> and
/// groups by <c>TenantId</c> — the same cross-tenant pattern as <see cref="TenantProvisioningService"/>.
///
/// <para><b>REAL data only.</b> This service does NOT source per-tenant usage counters, SLA-probe history, or a
/// request-metrics store (there is no metrics backend feeding this dashboard). Rather than fabricate numbers,
/// it computes ONLY what real data sources support — the database, a DB connectivity probe, a Redis probe, and
/// the Hangfire monitoring API (via <see cref="IJobQueueMonitor"/>). The Storage, EmailSends AND ApiCalls usage
/// gauges ARE real (US-ADM-012 AC-4 + US-PLT-004 — cumulative stored bytes across the four size-bearing tables,
/// month-to-date sent emails off <see cref="NotificationDelivery"/>, and month-to-date API calls off the
/// <c>tenant_api_usage</c> aggregate populated by the API-call counter). Everything still without a real data
/// source (error rate %, P95 latency, 24h trend series, SLA uptime, the error-rate "Attention Required" queue) is
/// returned as null/empty with an explicit "RequiresObservabilityPipeline" status flag. See the DTO doc
/// comments — those fields are DEFERRED, not broken. (OpenTelemetry traces/metrics ARE wired in the API host
/// via <c>ObservabilityExtensions</c>; they just don't feed this per-tenant dashboard.)</para>
///
/// <para><b>Redis.</b> Redis is OPTIONAL in this codebase. When a Redis connection string is configured the
/// shared <see cref="IConnectionMultiplexer"/> (which backs <c>IDistributedCache</c> and the SignalR backplane)
/// is registered, so Redis health is a REAL probe (PING) — <see cref="DependencyHealthStatus.Healthy"/> or
/// <see cref="DependencyHealthStatus.Down"/>. When no connection string is configured there is nothing to ping
/// and it is reported <see cref="DependencyHealthStatus.NotConfigured"/> — the honest "optional and absent" state.</para>
///
/// <para><b>Audit (NFR-5/AC-5).</b> Every read writes an AuditLog row (Action="Monitoring.Viewed" for the
/// dashboard/health, "Monitoring.TenantViewed" with ResourceId=tenantId for a detail view). The audit payload
/// carries only aggregate/operational fields — NO PII (BR-2).</para>
/// </summary>
public sealed class PlatformMonitoringService : IPlatformMonitoringService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IJobQueueMonitor _jobQueue;
    private readonly IConfiguration _configuration;
    private readonly IConnectionMultiplexer? _redis;
    private readonly IGlitchTipMetricsClient? _glitchTip;
    private readonly ILogger<PlatformMonitoringService> _logger;

    public PlatformMonitoringService(
        AppDbContext db,
        ICurrentUser currentUser,
        IJobQueueMonitor jobQueue,
        IConfiguration configuration,
        ILogger<PlatformMonitoringService> logger,
        // Optional: only registered by the API host when a Redis connection string is configured (Redis is
        // optional here). Null ⇒ no Redis wired ⇒ NotConfigured; non-null ⇒ a real PING probe.
        IConnectionMultiplexer? redis = null,
        // TC-ADM-002-14/-15/-16: optional so the existing monitoring tests keep compiling; DI always supplies
        // it. Null (or unconfigured) => the error fields stay empty/null, i.e. honestly unavailable.
        IGlitchTipMetricsClient? glitchTip = null)
    {
        _db = db;
        _currentUser = currentUser;
        _jobQueue = jobQueue;
        _configuration = configuration;
        _redis = redis;
        _glitchTip = glitchTip;
        _logger = logger;
    }

    // ── AC-1: platform health summary ───────────────────────────────────────

    public async Task<Result<PlatformHealthDto>> GetPlatformHealthAsync(CancellationToken cancellationToken = default)
    {
        // DB connectivity probe (FR-6). CanConnectAsync never throws; it returns false when unreachable.
        var dbHealthy = await TryCanConnectAsync(cancellationToken);
        var databaseHealth = dbHealthy ? DependencyHealthStatus.Healthy : DependencyHealthStatus.Down;

        // Redis (optional): real PING when configured, NotConfigured when there is no connection string.
        var redisHealth = await ResolveRedisHealthAsync(cancellationToken);

        // Real cross-tenant aggregates. Guard against a down DB: if we can't connect, report a Down roll-up
        // with empty aggregates rather than throwing.
        var tenantsByStatus = new List<TenantStatusCountDto>();
        var activeTenantCount = 0;
        var totalActiveUsers = 0;

        if (dbHealthy)
        {
            var grouped = await _db.Tenants
                .IgnoreQueryFilters()
                .Where(t => !t.IsDeleted)
                .GroupBy(t => t.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            tenantsByStatus = grouped
                .Select(g => new TenantStatusCountDto(g.Status.ToString(), g.Count))
                .OrderBy(s => s.Status)
                .ToList();

            // "Active" tenants = Active or Trial (serving) status — operational, not deleted.
            activeTenantCount = grouped
                .Where(g => g.Status is TenantStatus.Active or TenantStatus.Trial)
                .Sum(g => g.Count);

            totalActiveUsers = await _db.Users
                .IgnoreQueryFilters()
                .CountAsync(u => u.IsActive, cancellationToken);
        }

        // Hangfire job queue (FR-5) — real when storage is available, else Available=false.
        var jobQueue = _jobQueue.GetSnapshot();

        var overall = MonitoringClassifiers.RollUpHealth(databaseHealth, redisHealth, jobQueue.Failed);

        // TC-ADM-002-14: 24h error volume, platform-wide. NOTE: this is a COUNT, not a percentage — a true
        // rate needs a request-total denominator, which arrives with the option-A latency meter. Reported here
        // rather than left null because an absolute error count is already actionable, and inventing a
        // denominator would be worse than reporting the number we actually have.
        // Collected and logged now so the wiring is proven end-to-end; NOT surfaced as a rate until a
        // denominator exists (see the AggregateErrorRatePercent note below).
        if (_glitchTip is { IsConfigured: true })
        {
            var now = DateTime.UtcNow;
            var trend = await _glitchTip.GetErrorTrendAsync(null, now.AddHours(-24), now, cancellationToken);
            if (trend.Count > 0)
                _logger.LogDebug("[Monitoring] GlitchTip 24h error count = {Count}", trend.Sum(p => p.Count));
        }

        var dto = new PlatformHealthDto(
            OverallStatus: overall,
            ActiveTenantCount: activeTenantCount,
            TotalActiveUsers: totalActiveUsers,
            TenantsByStatus: tenantsByStatus,
            DatabaseHealth: databaseHealth,
            RedisHealth: redisHealth,
            JobQueue: jobQueue,
            // TC-ADM-002-14: STILL NULL, deliberately. GlitchTip gives us the error COUNT over 24h, but a
            // RATE needs a request-total denominator, which does not exist until the option-A latency/volume
            // meter lands. Putting a count in a field named "...Percent" would render as a percentage in the
            // UI and be read as one — a mislabelled number is worse than an absent one. The count IS collected
            // (see errorCount24h) and is ready to divide the moment the denominator exists.
            AggregateErrorRatePercent: null,
            P95LatencyMs: null,                                    // STILL DEFERRED — GlitchTip exposes no duration API
            MetricsStatus: MonitoringStatus.RequiresObservabilityPipeline,
            GeneratedAtUtc: DateTime.UtcNow);

        await WriteAuditAsync("Monitoring.Viewed", resourceId: null, new
        {
            dto.OverallStatus,
            dto.ActiveTenantCount,
            dto.TotalActiveUsers,
            Database = dto.DatabaseHealth.ToString(),
            Redis = dto.RedisHealth.ToString(),
        }, cancellationToken);

        return Result.Success(dto);
    }

    // ── AC-2/AC-3: per-tenant usage + quota-breach queue ────────────────────

    public async Task<Result<TenantUsageDashboardDto>> GetTenantUsageAsync(
        TenantUsageFilter filter, CancellationToken cancellationToken = default)
    {
        // Tenant rows (cross-tenant, system context). FR-4 filters: status / plan / search / created-date range.
        var query = _db.Tenants.IgnoreQueryFilters().Where(t => !t.IsDeleted);

        // ISSUE-003: an unparseable status must be rejected (400), NOT silently ignored — silently dropping the
        // filter returns the FULL unfiltered tenant list, which a caller filtering on a typo'd status reads as
        // "everything matched". A blank/absent status is unchanged (returns all). Same class as ISSUE-184.
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            if (!Enum.TryParse<TenantStatus>(filter.Status, ignoreCase: true, out var status))
                return Result.Failure<TenantUsageDashboardDto>(
                    $"Unknown tenant status '{filter.Status}'.", 400, "invalid_status");
            query = query.Where(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Plan))
        {
            var plan = filter.Plan.Trim();
            query = query.Where(t => t.PlanId == plan);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(term) || t.Subdomain.ToLower().Contains(term));
        }

        if (filter.CreatedFromUtc is { } from)
            query = query.Where(t => t.CreatedAt >= from);
        if (filter.CreatedToUtc is { } to)
            query = query.Where(t => t.CreatedAt <= to);

        var tenants = await query
            .Select(t => new { t.Id, t.Name, t.Subdomain, t.Status, t.PlanId, t.MaxEmployees })
            .ToListAsync(cancellationToken);

        // Active employee counts per tenant — cross-tenant, grouped by TenantId (IgnoreQueryFilters).
        var employeeCounts = await _db.Employees
            .IgnoreQueryFilters()
            .Where(e => e.IsActive && !e.IsDeleted)
            .GroupBy(e => e.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var countByTenant = employeeCounts.ToDictionary(x => x.TenantId, x => x.Count);

        // Fall back to the SubscriptionPlan limit by matching Tenant.PlanId == SubscriptionPlan.Code when the
        // tenant has no MaxEmployees snapshot (older tenants provisioned before US-ADM-001 stamped it). Also
        // carry the storage / email plan limits for the AC-4 usage gauges.
        var planLimits = await _db.SubscriptionPlans
            .Select(p => new { p.Code, p.MaxEmployees, p.MaxStorageGb, p.MaxEmailSendsPerMonth, p.MaxApiCallsPerMonth })
            .ToListAsync(cancellationToken);
        var limitByPlanCode = planLimits.ToDictionary(
            p => p.Code, StringComparer.OrdinalIgnoreCase);

        // Per-tenant plan-limit overrides (cross-tenant; PlanLimitOverride has NO tenant query filter). Grouped
        // so each tenant's gauge resolves override > plan via the shared PlanLimitResolver (US-ADM-009 FR-4).
        var overridesByTenant = (await _db.PlanLimitOverrides
                .ToListAsync(cancellationToken))
            .GroupBy(o => o.TenantId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PlanLimitOverride>)g.ToList());

        // AC-4 usage gauges: real cumulative stored bytes (all four size-bearing tables) and month-to-date sent
        // emails, per tenant. Both use the shared anti-drift helpers; both are cross-tenant grouped queries here
        // because the monitoring service runs with no resolved tenant.
        var storageByTenant = await TenantStorageUsage.ComputeBytesByTenantAsync(_db, cancellationToken: cancellationToken);
        var nowUtc = DateTime.UtcNow;
        var emailByTenant = await TenantEmailSendUsage.CountSentThisMonthByTenantAsync(_db, nowUtc, cancellationToken: cancellationToken);
        // US-PLT-004: real month-to-date API-call usage per tenant (persisted by the counter flusher).
        var apiCallsByTenant = await TenantApiCallUsage.CountThisMonthByTenantAsync(_db, nowUtc, cancellationToken: cancellationToken);

        var summaries = new List<TenantUsageSummaryDto>(tenants.Count);
        foreach (var t in tenants)
        {
            var active = countByTenant.TryGetValue(t.Id, out var c) ? c : 0;
            var plan = limitByPlanCode.GetValueOrDefault(t.PlanId);

            int? limit = t.MaxEmployees ?? plan?.MaxEmployees;

            var overrides = overridesByTenant.GetValueOrDefault(t.Id);
            var storageGauge = BuildStorageGauge(
                storageByTenant.GetValueOrDefault(t.Id),
                ResolveLongLimit(PlanLimitKeys.MaxStorageGb, plan?.MaxStorageGb, overrides, nowUtc));
            var emailGauge = BuildEmailGauge(
                emailByTenant.GetValueOrDefault(t.Id),
                (int?)ResolveLongLimit(PlanLimitKeys.MaxEmailSendsPerMonth, plan?.MaxEmailSendsPerMonth, overrides, nowUtc));
            var apiGauge = BuildApiCallsGauge(
                apiCallsByTenant.GetValueOrDefault(t.Id),
                ResolveLongLimit(PlanLimitKeys.MaxApiCallsPerMonth, plan?.MaxApiCallsPerMonth, overrides, nowUtc));

            summaries.Add(BuildSummary(
                t.Id, t.Name, t.Subdomain, t.Status.ToString(), t.PlanId, active, limit, storageGauge, emailGauge, apiGauge));
        }

        // Quota-breach queue (FR-3): tenants >= 80% on the employee limit, sorted by severity (percent) desc.
        var breachQueue = summaries
            .Where(s => MonitoringClassifiers.IsQuotaWarning(s.UsagePercent))
            .OrderByDescending(s => s.UsagePercent)
            .ToList();

        var dto = new TenantUsageDashboardDto(
            Tenants: summaries.OrderBy(s => s.Name).ToList(),
            QuotaBreachQueue: breachQueue,
            AttentionRequiredQueue: Array.Empty<TenantUsageSummaryDto>(),  // DEFERRED — no error-rate metric
            AttentionQueueStatus: MonitoringStatus.RequiresObservabilityPipeline,
            GeneratedAtUtc: DateTime.UtcNow);

        await WriteAuditAsync("Monitoring.Viewed", resourceId: null, new
        {
            TenantCount = summaries.Count,
            QuotaBreachCount = breachQueue.Count,
            Filter = new { filter.Status, filter.Plan, HasSearch = !string.IsNullOrWhiteSpace(filter.Search) },
        }, cancellationToken);

        return Result.Success(dto);
    }

    // ── AC-4: per-tenant operational detail ─────────────────────────────────

    public async Task<Result<TenantMonitoringDetailDto>> GetTenantDetailAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        var t = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(x => x.Id == tenantId && !x.IsDeleted)
            .Select(x => new { x.Id, x.Name, x.Subdomain, x.Status, x.PlanId, x.MaxEmployees, x.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (t is null)
            return Result.Failure<TenantMonitoringDetailDto>("Tenant not found.", 404, "tenant_not_found");

        var activeEmployees = await _db.Employees
            .IgnoreQueryFilters()
            .CountAsync(e => e.TenantId == tenantId && e.IsActive && !e.IsDeleted, cancellationToken);

        var plan = await _db.SubscriptionPlans
            .Where(p => p.Code == t.PlanId)
            .Select(p => new { p.MaxEmployees, p.MaxStorageGb, p.MaxEmailSendsPerMonth, p.MaxApiCallsPerMonth })
            .FirstOrDefaultAsync(cancellationToken);

        int? limit = t.MaxEmployees ?? plan?.MaxEmployees;

        // AC-4 usage gauges for this one tenant (same shared, anti-drift helpers; onlyTenant-scoped scans).
        var overrides = await _db.PlanLimitOverrides
            .Where(o => o.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        var nowUtc = DateTime.UtcNow;
        var usedBytes = (await TenantStorageUsage.ComputeBytesByTenantAsync(_db, tenantId, cancellationToken))
            .GetValueOrDefault(tenantId);
        var emailsSent = (await TenantEmailSendUsage.CountSentThisMonthByTenantAsync(_db, nowUtc, tenantId, cancellationToken))
            .GetValueOrDefault(tenantId);
        var apiCalls = (await TenantApiCallUsage.CountThisMonthByTenantAsync(_db, nowUtc, tenantId, cancellationToken))
            .GetValueOrDefault(tenantId);

        var storageGauge = BuildStorageGauge(
            usedBytes, ResolveLongLimit(PlanLimitKeys.MaxStorageGb, plan?.MaxStorageGb, overrides, nowUtc));

        // TC-ADM-002-17: uptime from the retained probe history. Stays NULL when there is no history — the TC
        // forbids a fabricated figure, and "no data" is a different statement from "100% up".
        var slaUptimePercent = await ComputeSlaUptimePercentAsync(nowUtc, cancellationToken);

        // TC-ADM-002-15/-16: fail-soft by contract — an unreachable or unconfigured GlitchTip yields an empty
        // list, which the DTO carries as "not available" rather than a reassuring zero.
        var topErrors = _glitchTip is null
            ? Array.Empty<object>()
            : (await _glitchTip.GetTopErrorsAsync(tenantId, 10, cancellationToken)).Cast<object>().ToArray();
        var emailGauge = BuildEmailGauge(
            emailsSent, (int?)ResolveLongLimit(PlanLimitKeys.MaxEmailSendsPerMonth, plan?.MaxEmailSendsPerMonth, overrides, nowUtc));
        var apiGauge = BuildApiCallsGauge(
            apiCalls, ResolveLongLimit(PlanLimitKeys.MaxApiCallsPerMonth, plan?.MaxApiCallsPerMonth, overrides, nowUtc));

        var summary = BuildSummary(
            t.Id, t.Name, t.Subdomain, t.Status.ToString(), t.PlanId, activeEmployees, limit, storageGauge, emailGauge, apiGauge);
        var employeeGauge = summary.Gauges.First(g => g.Resource == "Employees");

        // Owner email (operational, not PII per BR-2): the user assigned the Tenant Owner role for this tenant.
        var ownerEmail = await (
            from utr in _db.UserTenantRoles.IgnoreQueryFilters()
            join role in _db.Roles.IgnoreQueryFilters() on utr.RoleId equals role.Id
            join ut in _db.UserTenants.IgnoreQueryFilters() on utr.UserTenantId equals ut.Id
            join u in _db.Users.IgnoreQueryFilters() on ut.UserId equals u.Id
            where ut.TenantId == tenantId && role.Name == PermissionCatalog.BuiltInRoles.TenantOwner
            select u.Email)
            .FirstOrDefaultAsync(cancellationToken);

        // Last activity: most recent audit-log timestamp for this tenant (or null when there is none).
        var lastActivityAt = await _db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => (DateTime?)a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var dto = new TenantMonitoringDetailDto(
            TenantId: t.Id,
            Name: t.Name,
            Subdomain: t.Subdomain,
            Status: t.Status.ToString(),
            Plan: t.PlanId,
            OwnerEmail: ownerEmail,
            CreatedAt: t.CreatedAt,
            LastActivityAt: lastActivityAt,
            EmployeeUsage: employeeGauge,
            JobQueue: _jobQueue.GetSnapshot(),
            // TC-ADM-002-16: top errors come from GlitchTip, filtered by the tenant_id tag (verified to
            // DISCRIMINATE, not merely return 200). Empty = unavailable, NOT "zero errors".
            ErrorRateTrend24h: Array.Empty<object>(),              // per-tenant trend unavailable — see note in the client
            LatencyTrend24h: Array.Empty<object>(),                // STILL DEFERRED — GlitchTip has no duration API
            TopErrors: topErrors,
            SlaUptimePercent: slaUptimePercent,                    // TC-ADM-002-17 — null until probes exist
            MetricsStatus: MonitoringStatus.RequiresObservabilityPipeline,
            GeneratedAtUtc: DateTime.UtcNow);

        await WriteAuditAsync("Monitoring.TenantViewed", resourceId: tenantId.ToString(), new
        {
            TenantId = tenantId,
            t.Subdomain,
            Status = t.Status.ToString(),
            ActiveEmployees = activeEmployees,
            EmployeeBand = employeeGauge.Band?.ToString(),
        }, cancellationToken);

        return Result.Success(dto);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a per-tenant usage summary. The Employees, Storage, EmailSends AND ApiCalls gauges are now all REAL
    /// (US-ADM-012 AC-4 + US-PLT-004): the caller supplies the already-built Storage/Email/ApiCalls gauges (they
    /// require per-tenant DB reads via the shared anti-drift helpers).
    /// </summary>
    private static TenantUsageSummaryDto BuildSummary(
        Guid tenantId, string name, string subdomain, string status, string plan, int activeEmployees, int? limit,
        UsageGaugeDto storageGauge, UsageGaugeDto emailGauge, UsageGaugeDto apiGauge)
    {
        var percent = MonitoringClassifiers.ComputePercent(activeEmployees, limit);
        var band = percent is { } p ? MonitoringClassifiers.ClassifyBand(p) : (UsageBand?)null;

        var gauges = new List<UsageGaugeDto>
        {
            new("Employees", Available: true, Used: activeEmployees, Limit: limit, UsagePercent: percent, Band: band),
            storageGauge,
            apiGauge,
            emailGauge,
        };

        return new TenantUsageSummaryDto(
            TenantId: tenantId,
            Name: name,
            Subdomain: subdomain,
            Status: status,
            Plan: plan,
            ActiveEmployees: activeEmployees,
            EmployeeLimit: limit,
            UsagePercent: percent,
            Band: band,
            LimitKnown: limit is not null,
            Gauges: gauges);
    }

    /// <summary>
    /// Resolves the effective limit for a plan-limit key (override &gt; plan; null = UNLIMITED per BR-3) via the
    /// shared <see cref="PlanLimitResolver"/>, so the gauge and any enforcement gate resolve limits identically.
    /// </summary>
    private static long? ResolveLongLimit(
        string key, int? planValue, IEnumerable<PlanLimitOverride>? overrides, DateTime nowUtc)
        => PlanLimitResolver.Resolve(key, planValue, overrides, nowUtc).Value;

    /// <summary>
    /// The REAL storage gauge (US-ADM-012 AC-4). <paramref name="usedBytes"/> is the cumulative stored bytes across
    /// all four size-bearing tables (<see cref="TenantStorageUsage"/>); <paramref name="limitGb"/> is the resolved
    /// <c>max_storage_gb</c> cap (null ⇒ UNLIMITED per BR-3). <c>Used</c>/<c>Limit</c> are expressed in MEGABYTES
    /// (the gauge carries no unit field and byte counts overflow <see cref="int"/>); the percentage is computed on
    /// exact BYTES. A null limit is represented as real usage with a null limit/percent — never a divide-by-null.
    /// </summary>
    private static UsageGaugeDto BuildStorageGauge(long usedBytes, long? limitGb)
    {
        const long bytesPerMb = 1024L * 1024L;
        var usedMb = (int)Math.Min(usedBytes / bytesPerMb, int.MaxValue);

        if (limitGb is not { } gb)
            return new UsageGaugeDto("Storage", Available: true, Used: usedMb, Limit: null, UsagePercent: null, Band: null);

        var limitBytes = gb * 1024L * bytesPerMb;
        var limitMb = (int)Math.Min(gb * 1024L, int.MaxValue);
        var percent = limitBytes <= 0
            ? (usedBytes > 0 ? 100d : 0d)
            : Math.Round((double)usedBytes / limitBytes * 100d, 1);
        return new UsageGaugeDto("Storage", Available: true, Used: usedMb, Limit: limitMb,
            UsagePercent: percent, Band: MonitoringClassifiers.ClassifyBand(percent));
    }

    /// <summary>
    /// The REAL email-sends gauge (US-ADM-012 AC-4): month-to-date successful Email sends vs the resolved
    /// <c>max_email_sends_per_month</c> cap (null ⇒ UNLIMITED per BR-3 ⇒ null percent/band, never divide-by-null).
    /// </summary>
    private static UsageGaugeDto BuildEmailGauge(int sentThisMonth, int? limit)
    {
        var percent = MonitoringClassifiers.ComputePercent(sentThisMonth, limit);
        var band = percent is { } p ? MonitoringClassifiers.ClassifyBand(p) : (UsageBand?)null;
        return new UsageGaugeDto("EmailSends", Available: true, Used: sentThisMonth, Limit: limit,
            UsagePercent: percent, Band: band);
    }

    /// <summary>
    /// The REAL API-calls gauge (US-PLT-004): month-to-date persisted API-call count vs the resolved
    /// <c>max_api_calls_per_month</c> cap (null ⇒ UNLIMITED per BR-3 ⇒ null percent/band, never divide-by-null).
    /// The count lags real-time by at most one flush interval (buffered increments not yet persisted). Counts are
    /// <c>long</c>; the gauge's <c>Used</c>/<c>Limit</c> are <see cref="int"/>, so both are clamped to
    /// <see cref="int.MaxValue"/> for display while the percentage is computed on the exact <c>long</c> values.
    /// </summary>
    /// <summary>
    /// TC-ADM-002-17 / FR-7: platform uptime over the SLA window, as healthy-probes / total-probes.
    ///
    /// <para>Returns <c>null</c> when the window contains NO probes. That is deliberate and the TC requires it:
    /// with nothing measured, "100%" would be a fabrication and "0%" would be a false alarm. Null renders as the
    /// "Not available" placeholder, which is an honest statement about the absence of data.</para>
    ///
    /// <para>Uptime is a PLATFORM property — the API instance is shared — so the same figure is returned for
    /// every tenant. What is per-tenant is only the comparison against their plan's SLA tier.</para>
    /// </summary>
    private Task<double?> ComputeSlaUptimePercentAsync(DateTime nowUtc, CancellationToken cancellationToken)
        => ComputeSlaUptimePercentAsync(_db, nowUtc, SlaUptimeWindowDays, cancellationToken);

    /// <summary>
    /// The windowed query behind the uptime figure, taking its DbContext explicitly so a test can exercise the
    /// REAL query — including the window filter — rather than re-implementing it and proving nothing.
    /// </summary>
    internal static async Task<double?> ComputeSlaUptimePercentAsync(
        AppDbContext db, DateTime nowUtc, int windowDays, CancellationToken cancellationToken)
    {
        var windowStart = nowUtc.AddDays(-windowDays);

        var counts = await db.HealthProbes
            .AsNoTracking()
            .Where(p => p.ObservedAtUtc >= windowStart)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Healthy = g.Count(p => p.IsHealthy) })
            .FirstOrDefaultAsync(cancellationToken);

        return UptimeFromCounts(counts?.Total ?? 0, counts?.Healthy ?? 0);
    }

    /// <summary>
    /// TC-ADM-002-17: the uptime rule itself, pure over the two counts so it can be exercised directly rather
    /// than re-implemented in a test. <b>Zero probes ⇒ null</b>, never 100 — with nothing measured, a perfect
    /// score is a fabrication and a zero score is a false alarm; "not available" is the only honest answer.
    /// </summary>
    internal static double? UptimeFromCounts(int total, int healthy)
    {
        if (total <= 0)
            return null;

        return Math.Round((double)healthy / total * 100d, 3);
    }

    /// <summary>The SLA measurement window, in days. Matches the default probe retention so the window can
    /// always be fully populated.</summary>
    private const int SlaUptimeWindowDays = 30;

    private static UsageGaugeDto BuildApiCallsGauge(long usedThisMonth, long? limit)
    {
        var usedForDisplay = (int)Math.Min(usedThisMonth, int.MaxValue);

        if (limit is not { } cap)
            return new UsageGaugeDto("ApiCalls", Available: true, Used: usedForDisplay, Limit: null,
                UsagePercent: null, Band: null);

        var limitForDisplay = (int)Math.Min(cap, int.MaxValue);
        var percent = cap <= 0
            ? (usedThisMonth > 0 ? 100d : 0d)
            : Math.Round((double)usedThisMonth / cap * 100d, 1);
        return new UsageGaugeDto("ApiCalls", Available: true, Used: usedForDisplay, Limit: limitForDisplay,
            UsagePercent: percent, Band: MonitoringClassifiers.ClassifyBand(percent));
    }

    private async Task<bool> TryCanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _db.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Platform health DB connectivity probe failed.");
            return false;
        }
    }

    /// <summary>
    /// Real Redis health (ISSUE-344). Redis is OPTIONAL: when no connection string is configured there is no
    /// multiplexer to ping ⇒ <see cref="DependencyHealthStatus.NotConfigured"/> (must NOT be reported as Down —
    /// absence is not a failure). When configured, the shared multiplexer is PINGed: reachable ⇒ Healthy,
    /// unreachable/timeout ⇒ Down. Mirrors the readiness health check registered in <c>Program.cs</c>.
    /// </summary>
    private async Task<DependencyHealthStatus> ResolveRedisHealthAsync(CancellationToken cancellationToken)
    {
        var redisConfigured = !string.IsNullOrWhiteSpace(
            _configuration.GetConnectionString("Redis") ?? _configuration["Redis:ConnectionString"]);

        // Genuinely not configured (Redis is optional) ⇒ NotConfigured, never Down.
        if (!redisConfigured || _redis is null)
            return DependencyHealthStatus.NotConfigured;

        try
        {
            await _redis.GetDatabase().PingAsync();
            return DependencyHealthStatus.Healthy;
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogWarning(ex, "Platform health Redis connectivity probe failed.");
            return DependencyHealthStatus.Down;
        }
    }

    /// <summary>
    /// Writes a monitoring-access audit row (NFR-5/AC-5). The payload is serialized aggregate/operational data
    /// only — never employee names/salaries/PII (BR-2). Audited in the system context: TenantId is the viewed
    /// tenant for a detail view (set on ResourceId) but left null on the row itself for the platform-wide views.
    /// </summary>
    private async Task WriteAuditAsync(string action, string? resourceId, object payload, CancellationToken ct)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = null,                                       // platform/system-scoped monitoring access
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "Monitoring",
            ResourceId = resourceId,
            After = JsonSerializer.Serialize(payload),             // aggregates only — no PII (BR-2)
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
    }
}
