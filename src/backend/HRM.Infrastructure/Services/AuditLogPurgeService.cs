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

    public async Task<int> PurgeExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var tenants = await _db.Tenants
            .IgnoreQueryFilters()
            .Select(t => new { t.Id, t.AuditLogRetentionDays })
            .ToListAsync(cancellationToken);

        var totalDeleted = 0;

        foreach (var tenant in tenants)
        {
            var retentionDays = tenant.AuditLogRetentionDays <= 0 ? 90 : tenant.AuditLogRetentionDays;
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

        if (totalDeleted > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return totalDeleted;
    }
}
