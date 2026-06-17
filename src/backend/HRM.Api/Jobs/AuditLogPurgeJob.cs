using HRM.Application.Common.Interfaces;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-ADM-008 (FR-6/BR-5): daily Hangfire job that purges audit-log rows older than each tenant's
/// plan-governed retention window and records a system-scoped audit of the purge. Thin wrapper — the
/// InMemory-testable core lives in <see cref="IAuditLogPurgeService"/>.
/// </summary>
public sealed class AuditLogPurgeJob
{
    private readonly IAuditLogPurgeService _purgeService;

    public AuditLogPurgeJob(IAuditLogPurgeService purgeService)
    {
        _purgeService = purgeService;
    }

    public async Task RunAsync()
    {
        Log.Information("Starting AuditLogPurgeJob");
        var deleted = await _purgeService.PurgeExpiredAsync(DateTime.UtcNow);
        Log.Information("Completed AuditLogPurgeJob — purged {Count} audit records", deleted);
    }
}
