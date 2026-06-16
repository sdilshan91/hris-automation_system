using HRM.Application.Common.Models;
using HRM.Application.Features.Monitoring.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// System-admin platform monitoring (US-ADM-002). Runs in the system/admin context (no resolved tenant) and
/// aggregates ACROSS tenants — every tenant-scoped query uses <c>IgnoreQueryFilters()</c> and groups by
/// <c>TenantId</c> (same pattern as <see cref="ITenantProvisioningService"/>). Every read writes a monitoring
/// audit row (NFR-5) carrying NO PII (BR-2). Metrics with no real backing data source (error rate, latency,
/// SLA, usage counters other than employees) are returned as null/empty with explicit deferred status flags.
/// </summary>
public interface IPlatformMonitoringService
{
    /// <summary>Platform health summary (AC-1/FR-1/FR-6): real aggregate counts + DB/Redis/Hangfire signals + roll-up.</summary>
    Task<Result<PlatformHealthDto>> GetPlatformHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>Per-tenant usage dashboard (AC-2/FR-2/FR-3): real employee gauges + quota-breach queue, filtered (FR-4).</summary>
    Task<Result<TenantUsageDashboardDto>> GetTenantUsageAsync(
        TenantUsageFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Per-tenant operational detail (AC-4): real status/plan/owner/last-activity/job/employee-gauge.</summary>
    Task<Result<TenantMonitoringDetailDto>> GetTenantDetailAsync(
        Guid tenantId, CancellationToken cancellationToken = default);
}
