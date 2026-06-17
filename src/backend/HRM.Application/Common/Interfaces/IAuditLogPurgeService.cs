namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-008 (FR-6/BR-5): the cross-tenant audit-log retention purge. Deletes <c>audit_logs</c> rows older than
/// each tenant's <c>AuditLogRetentionDays</c> window and writes a system-scoped audit row recording the purge
/// (FR-6 — "purge logged in system_audit_log"; this platform reuses the single audit table with a system action).
///
/// <para>Runs OUTSIDE a resolved tenant (a recurring Hangfire job), so it ignores the EF tenant context and
/// scopes per-tenant explicitly. The core logic is provider-agnostic so it is InMemory-testable.</para>
/// </summary>
public interface IAuditLogPurgeService
{
    /// <summary>
    /// Purges expired audit rows for every tenant and returns the total number deleted. <paramref name="nowUtc"/>
    /// is injectable so tests can seed deterministic timestamps without sleeping.
    /// </summary>
    Task<int> PurgeExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
