using HRM.Application.Common.Interfaces;

namespace HRM.Api.Jobs;

/// <summary>
/// Hangfire wrapper for the field-encryption key-rotation re-encryption sweep (runbook step 4,
/// <c>HRM.Infrastructure/Security/README.md</c>): every value stored under a NON-active ring key is decrypted
/// with the ring and re-written under <c>Encryption:ActiveKeyId</c>. Enqueued on demand by
/// <c>POST /api/v1/system/encryption/reencrypt</c> right after an ops key rotation — deliberately NOT a
/// recurring job (it only has work when the active key just changed); in dev it can also be fired from the
/// Hangfire dashboard.
///
/// <para>All logic (tenant enumeration + per-tenant <c>ITenantJobRunner</c> scoping so RLS-ON never
/// fail-closes the scan to 0 rows, batching, corrupt-row skip, per-keyId BEFORE/AFTER report) lives in
/// <see cref="IFieldEncryptionMaintenanceService"/>. Idempotent / re-run safe. The returned summary is stored
/// by Hangfire and the per-keyId counts are logged — retire the old key ONLY when its AFTER count is 0.</para>
/// </summary>
public sealed class ReencryptFieldKeyJob
{
    private readonly IFieldEncryptionMaintenanceService _maintenance;

    public ReencryptFieldKeyJob(IFieldEncryptionMaintenanceService maintenance)
    {
        _maintenance = maintenance;
    }

    public Task<FieldReencryptionSummaryDto> RunAsync() => _maintenance.ReencryptAsync(CancellationToken.None);
}
