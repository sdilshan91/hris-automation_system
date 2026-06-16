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

namespace HRM.Infrastructure.Services;

/// <summary>
/// System-admin platform monitoring (US-ADM-002). Runs in the system/admin context (no resolved tenant), so
/// every tenant-scoped query uses <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/> and
/// groups by <c>TenantId</c> — the same cross-tenant pattern as <see cref="TenantProvisioningService"/>.
///
/// <para><b>REAL data only.</b> This platform does NOT run an observability pipeline (no OpenTelemetry metrics,
/// no per-tenant Redis usage counters, no SLA-probe history, no metrics store). Rather than fabricate numbers,
/// this service computes ONLY what real data sources support — the database, a DB connectivity probe, and the
/// Hangfire monitoring API (via <see cref="IJobQueueMonitor"/>). Everything else (error rate %, P95 latency,
/// 24h trend series, SLA uptime, storage/API/email usage gauges, the error-rate "Attention Required" queue) is
/// returned as null/empty with an explicit "RequiresObservabilityPipeline" status flag. See the DTO doc
/// comments — those fields are DEFERRED, not broken.</para>
///
/// <para><b>Redis.</b> A Redis connection string exists in config but NO Redis client / IDistributedCache is
/// registered in DI in this codebase (the permission cache is in-memory). So Redis health is reported
/// <see cref="DependencyHealthStatus.NotConfigured"/> rather than pinged — honest, not fabricated.</para>
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
    private readonly ILogger<PlatformMonitoringService> _logger;

    public PlatformMonitoringService(
        AppDbContext db,
        ICurrentUser currentUser,
        IJobQueueMonitor jobQueue,
        IConfiguration configuration,
        ILogger<PlatformMonitoringService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _jobQueue = jobQueue;
        _configuration = configuration;
        _logger = logger;
    }

    // ── AC-1: platform health summary ───────────────────────────────────────

    public async Task<Result<PlatformHealthDto>> GetPlatformHealthAsync(CancellationToken cancellationToken = default)
    {
        // DB connectivity probe (FR-6). CanConnectAsync never throws; it returns false when unreachable.
        var dbHealthy = await TryCanConnectAsync(cancellationToken);
        var databaseHealth = dbHealthy ? DependencyHealthStatus.Healthy : DependencyHealthStatus.Down;

        // Redis: configured-but-not-wired in this codebase ⇒ NotConfigured (no client to ping). Honest, not faked.
        var redisHealth = ResolveRedisHealth();

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

        var dto = new PlatformHealthDto(
            OverallStatus: overall,
            ActiveTenantCount: activeTenantCount,
            TotalActiveUsers: totalActiveUsers,
            TenantsByStatus: tenantsByStatus,
            DatabaseHealth: databaseHealth,
            RedisHealth: redisHealth,
            JobQueue: jobQueue,
            AggregateErrorRatePercent: null,                       // DEFERRED — no metrics store
            P95LatencyMs: null,                                    // DEFERRED — no metrics store
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

        if (!string.IsNullOrWhiteSpace(filter.Status)
            && Enum.TryParse<TenantStatus>(filter.Status, ignoreCase: true, out var status))
        {
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
        // tenant has no MaxEmployees snapshot (older tenants provisioned before US-ADM-001 stamped it).
        var planLimits = await _db.SubscriptionPlans
            .Select(p => new { p.Code, p.MaxEmployees })
            .ToListAsync(cancellationToken);
        var limitByPlanCode = planLimits.ToDictionary(
            p => p.Code, p => p.MaxEmployees, StringComparer.OrdinalIgnoreCase);

        var summaries = new List<TenantUsageSummaryDto>(tenants.Count);
        foreach (var t in tenants)
        {
            var active = countByTenant.TryGetValue(t.Id, out var c) ? c : 0;

            int? limit = t.MaxEmployees
                ?? (limitByPlanCode.TryGetValue(t.PlanId, out var planLimit) ? planLimit : null);

            summaries.Add(BuildSummary(t.Id, t.Name, t.Subdomain, t.Status.ToString(), t.PlanId, active, limit));
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

        int? limit = t.MaxEmployees;
        if (limit is null)
        {
            limit = await _db.SubscriptionPlans
                .Where(p => p.Code == t.PlanId)
                .Select(p => p.MaxEmployees)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var summary = BuildSummary(t.Id, t.Name, t.Subdomain, t.Status.ToString(), t.PlanId, activeEmployees, limit);
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
            ErrorRateTrend24h: Array.Empty<object>(),              // DEFERRED — no metrics store
            LatencyTrend24h: Array.Empty<object>(),                // DEFERRED — no metrics store
            TopErrors: Array.Empty<object>(),                      // DEFERRED — no metrics store
            SlaUptimePercent: null,                                // DEFERRED — no probe history
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
    /// Builds a per-tenant usage summary. The employee gauge is REAL; storage / API-calls / email-sends gauges
    /// are DEFERRED (Available=false, null values) — no usage counters are instrumented in this codebase.
    /// </summary>
    private static TenantUsageSummaryDto BuildSummary(
        Guid tenantId, string name, string subdomain, string status, string plan, int activeEmployees, int? limit)
    {
        var percent = MonitoringClassifiers.ComputePercent(activeEmployees, limit);
        var band = percent is { } p ? MonitoringClassifiers.ClassifyBand(p) : (UsageBand?)null;

        var gauges = new List<UsageGaugeDto>
        {
            new("Employees", Available: true, Used: activeEmployees, Limit: limit, UsagePercent: percent, Band: band),
            // DEFERRED gauges — no counters instrumented:
            new("Storage", Available: false, Used: null, Limit: null, UsagePercent: null, Band: null),
            new("ApiCalls", Available: false, Used: null, Limit: null, UsagePercent: null, Band: null),
            new("EmailSends", Available: false, Used: null, Limit: null, UsagePercent: null, Band: null),
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
    /// Redis is configured (connection string present) but NO Redis client / IDistributedCache is registered in
    /// DI in this codebase, so there is nothing to ping — report NotConfigured. If a Redis client is wired up
    /// later, this is where a real PING would go.
    /// </summary>
    private DependencyHealthStatus ResolveRedisHealth()
    {
        // No Redis client / IDistributedCache is registered in DI, so there is nothing to ping regardless of
        // whether a connection string is present. Always NotConfigured — honest, never a fabricated "Healthy".
        _ = _configuration; // (kept injected: a real PING would read the connection string here once wired)
        return DependencyHealthStatus.NotConfigured;
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
