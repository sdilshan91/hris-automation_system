namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-004 (AC-4 / FR-3 / NFR-2 / NFR-4): hard-deletes all per-tenant data for a tenant whose termination
/// grace period has expired, then marks the tenant <c>Terminated</c> with PII redacted (the tenant row +
/// audit logs are retained). The deletion is GENERIC (it enumerates every tenant-scoped entity in the EF
/// model, child-first by FK dependency) and TENANT-SCOPED (only the target tenant's rows are touched).
///
/// <para>Invoked by the Hangfire <c>TenantDeletionJob</c> at the scheduled fire-time, or directly in tests.
/// Idempotent: if the tenant is not (or no longer) in <c>Terminating</c> status it no-ops.</para>
/// </summary>
public interface ITenantDataDeletionService
{
    /// <summary>
    /// Executes the hard-delete + tenant redaction for <paramref name="tenantId"/>. Returns the number of
    /// tenant-scoped entity types processed (for logging/telemetry), or a failure if the tenant is missing or
    /// not in <c>Terminating</c> status.
    /// </summary>
    Task<Models.Result<TenantDeletionSummary>> DeleteTenantDataAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a tenant data deletion (US-ADM-004 AC-4) — for logging/audit.</summary>
public sealed record TenantDeletionSummary(Guid TenantId, int EntityTypesDeleted, long RowsDeleted);
