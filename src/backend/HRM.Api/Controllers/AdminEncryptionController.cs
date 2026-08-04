using Hangfire;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// System Admin Console — field-encryption key-rotation operations (the P3-4 runbook's previously-manual
/// "re-encrypt the backlog" step, <c>HRM.Infrastructure/Security/README.md</c>). These endpoints run in the
/// SYSTEM/admin context and operate ACROSS tenants (the sweep/report enumerate all tenants via
/// <c>ITenantJobRunner</c> so RLS-ON never fail-closes them to 0 rows).
///
/// <para>Authorization is <c>Tenant.Lifecycle</c> — the strongest existing SYSTEM-level, DESTRUCTIVE
/// capability on the sibling admin controllers (US-ADM-004 pattern): only the platform SystemAdmin role
/// (seeded with every catalog permission) holds it; the read-only System Support role does NOT, and a
/// tenant-scoped role that nominally held it could never reach the admin-context endpoints. The GET report is
/// gated with the SAME permission: it enumerates ring keyIds (operational security metadata, not for
/// support eyes). The JWT carries roles in a custom "roles" claim, so the established permission-policy gate
/// is used rather than <c>[Authorize(Roles=...)]</c>.</para>
/// </summary>
[ApiController]
[Route("api/v1/system/encryption")]
[Authorize]
public sealed class AdminEncryptionController : ControllerBase
{
    private readonly IFieldEncryptionMaintenanceService _maintenance;
    private readonly IBackgroundJobClient? _backgroundJobs;
    // ISSUE-359: the FILE side of encryption-at-rest. Kept on the same route family so an operator has one
    // place to ask "what is still unencrypted", rather than two unrelated surfaces.
    private readonly IFileEncryptionMaintenanceService _files;

    /// <summary>
    /// <paramref name="backgroundJobs"/> is OPTIONAL (the repo's standard Hangfire seam): present in the real
    /// host → the sweep is ENQUEUED (202) so a large fleet never runs inside an HTTP timeout; absent (test
    /// hosts without Hangfire storage) → the sweep runs INLINE and returns its summary.
    /// </summary>
    public AdminEncryptionController(
        IFieldEncryptionMaintenanceService maintenance,
        IFileEncryptionMaintenanceService files,
        IBackgroundJobClient? backgroundJobs = null)
    {
        _maintenance = maintenance;
        _files = files;
        _backgroundJobs = backgroundJobs;
    }

    /// <summary>
    /// GET /api/v1/system/encryption/files — how much of the stored FILE estate is still plaintext (ISSUE-359).
    ///
    /// <para>The rollout tolerates legacy plaintext on read so encryption could ship without a migration. This
    /// count is what stops that tolerance becoming permanent: without a number somebody can look at, "we will
    /// back-fill later" is indistinguishable from "there is still salary in plaintext".</para>
    /// </summary>
    [HttpGet("files")]
    [RequirePermission("Tenant.Lifecycle")]
    [ProducesResponseType(typeof(ApiResponse<FileEncryptionStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFileEncryptionStatus(CancellationToken cancellationToken)
    {
        var result = await _files.GetStatusAsync(cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<FileEncryptionStatusDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/system/encryption/files/sweep — re-writes this tenant's remaining plaintext files through
    /// the encrypting storage. Idempotent: already-sealed files are skipped, so a re-run is safe.
    /// </summary>
    [HttpPost("files/sweep")]
    [RequirePermission("Tenant.Lifecycle")]
    [ProducesResponseType(typeof(ApiResponse<FileEncryptionSweepResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SweepFileEncryption(CancellationToken cancellationToken)
    {
        var result = await _files.SweepAsync(cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<FileEncryptionSweepResultDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/system/encryption/reencrypt — trigger the bulk re-encryption sweep (run this right after
    /// an ops key rotation flips <c>Encryption:ActiveKeyId</c>). Idempotent / re-run safe. Verify completion
    /// with the GET report: retire the old key ONLY when its count is 0.
    /// </summary>
    [HttpPost("reencrypt")]
    [RequirePermission("Tenant.Lifecycle")]
    [ProducesResponseType(typeof(ApiResponse<FieldReencryptionTriggerDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Reencrypt(CancellationToken cancellationToken)
    {
        if (_backgroundJobs is not null)
        {
            var jobId = _backgroundJobs.Enqueue<ReencryptFieldKeyJob>(job => job.RunAsync());
            return StatusCode(
                StatusCodes.Status202Accepted,
                ApiResponse<FieldReencryptionTriggerDto>.Ok(new FieldReencryptionTriggerDto("enqueued", jobId, null)));
        }

        var summary = await _maintenance.ReencryptAsync(cancellationToken);
        return StatusCode(
            StatusCodes.Status202Accepted,
            ApiResponse<FieldReencryptionTriggerDto>.Ok(new FieldReencryptionTriggerDto("inline", null, summary)));
    }

    /// <summary>
    /// GET /api/v1/system/encryption/report — READ-ONLY per-keyId row counts across every encrypted column
    /// (the key-retirement verification gate; plaintext residue surfaces under the <c>(plaintext)</c> pseudo
    /// key). Lets ops verify without triggering a re-encryption.
    /// </summary>
    [HttpGet("report")]
    [RequirePermission("Tenant.Lifecycle")]
    [ProducesResponseType(typeof(ApiResponse<EncryptionKeyUsageReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Report(CancellationToken cancellationToken)
    {
        var report = await _maintenance.GetKeyUsageReportAsync(cancellationToken);
        return Ok(ApiResponse<EncryptionKeyUsageReportDto>.Ok(report));
    }
}
