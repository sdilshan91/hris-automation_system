using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ADM-008 (FR-6/BR-5): cross-tenant audit-log retention purge. For each tenant, deletes <c>audit_logs</c>
/// rows whose <c>CreatedAt</c> is older than <c>now - tenant.AuditLogRetentionDays</c>, then writes ONE
/// system-scoped audit row per tenant recording how many rows were purged (FR-6).
///
/// <para>This runs OUTSIDE a request, so there is no resolved tenant context — it uses
/// <c>IgnoreQueryFilters()</c> and scopes EXPLICITLY by tenant id. Deletion is done with <c>RemoveRange</c> so it
/// is provider-agnostic (works on InMemory in tests and on PostgreSQL in production); a Postgres-only
/// <c>ExecuteDelete</c> optimization is a deferred follow-up for very large tables.</para>
///
/// <para>ISSUE-062: the per-tenant loop scopes by an explicit tenant id, so it can never reach the
/// SYSTEM-scoped rows (<c>TenantId == null</c>) written by platform monitoring, pre-tenant-resolution auth
/// failures, and the FR-7 platform copies of lockout/unlock. Those rows were retained forever. They are now
/// purged too, on their own window: <see cref="SystemRetentionDays"/>, defined as the LONGEST tenant
/// retention currently configured (floor <see cref="DefaultRetentionDays"/> days). A platform-wide row must
/// outlive every tenant's own copy, so taking the max — not the min or an average — is the safe direction;
/// a shorter window would delete the platform view of an event whose tenant copy is still live.</para>
/// </summary>
public sealed class AuditLogPurgeService : IAuditLogPurgeService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuditLogPurgeService> _logger;

    public AuditLogPurgeService(AppDbContext db, ILogger<AuditLogPurgeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Retention applied when a tenant has none configured (0 or negative).</summary>
    private const int DefaultRetentionDays = 90;

    public async Task<int> PurgeExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var tenants = await _db.Tenants
            .IgnoreQueryFilters()
            .Select(t => new { t.Id, t.AuditLogRetentionDays })
            .ToListAsync(cancellationToken);

        var totalDeleted = 0;

        foreach (var tenant in tenants)
        {
            var retentionDays = tenant.AuditLogRetentionDays <= 0 ? DefaultRetentionDays : tenant.AuditLogRetentionDays;
            var cutoff = nowUtc.AddDays(-retentionDays);

            var expired = await _db.AuditLogs
                .IgnoreQueryFilters()
                .Where(a => a.TenantId == tenant.Id && a.CreatedAt < cutoff)
                .ToListAsync(cancellationToken);

            if (expired.Count == 0)
                continue;

            _db.AuditLogs.RemoveRange(expired);

            // FR-6: log the purge itself as a system-scoped audit action (UserId null = system actor).
            _db.AuditLogs.Add(new AuditLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenant.Id,
                UserId = null,
                EventType = "AuditLog.Purge",
                Action = "AuditLog.Purge",
                ResourceType = "AuditLog",
                Detail = $"Purged {expired.Count} audit records older than {retentionDays} days (cutoff {cutoff:O}).",
                CreatedAt = nowUtc,
            });

            totalDeleted += expired.Count;
            _logger.LogInformation(
                "Purged {Count} audit records for tenant {TenantId} (retention {Days}d)",
                expired.Count, tenant.Id, retentionDays);
        }

        totalDeleted += await PurgeSystemScopedAsync(
            nowUtc, SystemRetentionDays(tenants.Select(t => t.AuditLogRetentionDays)), cancellationToken);

        if (totalDeleted > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return totalDeleted;
    }

    /// <summary>
    /// ISSUE-062: the retention window for SYSTEM-scoped (<c>TenantId == null</c>) rows — the longest tenant
    /// retention currently configured, floored at <see cref="DefaultRetentionDays"/>. A platform row is a
    /// cross-tenant view of an event, so it must not expire before any tenant's own copy of it.
    /// </summary>
    private static int SystemRetentionDays(IEnumerable<int> tenantRetentions)
    {
        var longest = DefaultRetentionDays;
        foreach (var days in tenantRetentions)
        {
            if (days > longest)
                longest = days;
        }
        return longest;
    }

    /// <summary>
    /// ISSUE-062: deletes expired SYSTEM-scoped audit rows (<c>TenantId == null</c>) — the class the
    /// per-tenant loop structurally cannot reach — and records the purge as one more system-scoped row.
    /// Returns the number of rows removed (the bookkeeping row it adds is not counted).
    /// </summary>
    private async Task<int> PurgeSystemScopedAsync(DateTime nowUtc, int retentionDays, CancellationToken ct)
    {
        var cutoff = nowUtc.AddDays(-retentionDays);

        var expired = await _db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == null && a.CreatedAt < cutoff)
            .ToListAsync(ct);

        if (expired.Count == 0)
            return 0;

        _db.AuditLogs.RemoveRange(expired);

        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = null,                 // platform/system-scoped, like the rows it just purged
            UserId = null,
            EventType = "AuditLog.PurgeSystem",
            Action = "AuditLog.PurgeSystem",
            ResourceType = "AuditLog",
            Detail = $"Purged {expired.Count} system-scoped audit records older than {retentionDays} days (cutoff {cutoff:O}).",
            CreatedAt = nowUtc,
        });

        _logger.LogInformation(
            "Purged {Count} system-scoped audit records (retention {Days}d)", expired.Count, retentionDays);

        return expired.Count;
    }
}
