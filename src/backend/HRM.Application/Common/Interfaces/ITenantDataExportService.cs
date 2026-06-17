using HRM.Application.Common.Models;
using HRM.Application.Features.DataExport.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-010: tenant data-export orchestration. Two entry points the story calls out:
///   - <see cref="InitiateAsync"/> validates the status gate + rate limit, creates the Queued ExportRequest,
///     audits "DataExport.Requested" and enqueues generation (Hangfire when wired, else the caller can invoke
///     <see cref="GenerateAsync"/> directly — which is what makes the whole thing testable without Hangfire).
///   - <see cref="GenerateAsync"/> does the heavy work: queries each selected tenant-scoped entity (AsNoTracking),
///     serializes to CSV (auth-secrets excluded), packages a ZIP with a manifest + audit JSONL, stores it,
///     and marks the request Completed (or Failed).
/// Both are tenant-aware: the Tenant-Admin path runs in the resolved-tenant context; the System-Admin path
/// restores the target tenant context for the scope (the generation always re-scopes explicitly by tenant id).
/// </summary>
public interface ITenantDataExportService
{
    /// <summary>
    /// Tenant-Admin initiation (AC-1): the target tenant is the resolved <c>ITenantContext.TenantId</c>; any
    /// client-supplied tenant id is ignored (AC-5).
    /// </summary>
    Task<Result<ExportInitiatedDto>> InitiateAsync(
        ExportInitiateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// System-Admin initiation (AC-6): the target tenant is the explicit <paramref name="tenantId"/>; a
    /// Suspended tenant is allowed here (BR-2), a Terminated tenant is not (BR-3).
    /// </summary>
    Task<Result<ExportInitiatedDto>> InitiateForTenantAsync(
        Guid tenantId, ExportInitiateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates the export bundle for a previously-created request (AC-2). Idempotent-ish: only acts on a
    /// Queued request. The tenant context must already be scoped to the request's tenant (the Hangfire job /
    /// test restores it), but every read additionally re-scopes by the request's tenant id (defence in depth).
    /// </summary>
    Task<Result<ExportRequestDto>> GenerateAsync(Guid exportRequestId, CancellationToken cancellationToken = default);

    /// <summary>History list for the current tenant, newest first (FR-9 / §8).</summary>
    Task<Result<IReadOnlyList<ExportRequestDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Single export-request status (FR-9). Tenant-scoped.</summary>
    Task<Result<ExportRequestDto>> GetAsync(Guid exportRequestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Serves the completed bundle (AC-3/FR-7): returns the bytes + filename when the request is Completed and
    /// not past <c>ExpiresAt</c>; audits "DataExport.Downloaded". After expiry it returns 410 Gone and the file
    /// is considered deleted.
    /// </summary>
    Task<Result<ExportDownloadDto>> DownloadAsync(Guid exportRequestId, CancellationToken cancellationToken = default);
}
