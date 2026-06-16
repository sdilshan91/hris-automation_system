using HRM.Application.Common.Models;
using HRM.Application.Features.Impersonation.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ADM-003: System Admin / System Support tenant-user impersonation. Runs in the system/admin context (no
/// resolved tenant) and operates across tenants — it queries the target tenant's users/roles with
/// <c>IgnoreQueryFilters</c> (system-context reads, exactly like tenant provisioning) and creates the
/// cross-tenant <c>ImpersonationSession</c> row + the impersonation JWT for the target user.
/// </summary>
public interface IImpersonationService
{
    /// <summary>
    /// Starts an impersonation session (AC-1/FR-1, BR-1..BR-5). Validates the target tenant + membership,
    /// enforces the business rules, determines read-only (System Support OR Suspended tenant), creates the
    /// session, writes the "Impersonation.Started" system audit, dispatches the tenant-admin notification, and
    /// returns the impersonation token + redirect URL.
    /// </summary>
    Task<Result<StartImpersonationResultDto>> StartAsync(
        StartImpersonationInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends an active impersonation session (AC-3). Sets the session Ended, writes the "Impersonation.Ended"
    /// audit with the actions-count summary. Only the impersonator who owns the session may end it.
    /// </summary>
    Task<Result<EndImpersonationResultDto>> EndAsync(
        Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a tenant's active users as impersonation targets for the picker (AC-1). Excludes system-admin /
    /// system-tenant users (BR-2).
    /// </summary>
    Task<Result<IReadOnlyList<ImpersonationTargetDto>>> ListTargetsAsync(
        Guid targetTenantId, CancellationToken cancellationToken = default);
}
