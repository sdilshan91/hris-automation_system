using HRM.Application.Common.Models;
using HRM.Application.Features.Tenants.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// System-admin tenant lifecycle service (US-ADM-004): suspend / terminate / reactivate / restore and the
/// lifecycle-history read. Runs in the system/admin context (no resolved tenant) and operates ACROSS tenants
/// (IgnoreQueryFilters + explicit id scoping), mirroring <see cref="ITenantProvisioningService"/> and
/// <see cref="IImpersonationService"/>.
///
/// <para>Every transition validates BR-1's allowed-state matrix (rejecting invalid transitions), refuses to
/// touch the system/platform tenant (BR-2), and writes a <c>TenantLifecycleEvent</c> + a system <c>AuditLog</c>
/// (FR-7) atomically. Notifications dispatch via the log-only <see cref="ITenantLifecycleNotificationService"/>
/// seam. Termination scheduling/cancellation goes through the optional <see cref="ITenantDeletionScheduler"/>
/// Hangfire seam.</para>
/// </summary>
public interface ITenantLifecycleService
{
    /// <summary>Suspends an active/past-due tenant (AC-1/FR-1): status → Suspended, revoke all refresh tokens, notify.</summary>
    Task<Result<TenantLifecycleResultDto>> SuspendAsync(SuspendTenantInput input, CancellationToken cancellationToken = default);

    /// <summary>Terminates a tenant into grace (AC-3/FR-2): status → Terminating, schedule deletion + reminders.</summary>
    Task<Result<TenantLifecycleResultDto>> TerminateAsync(TerminateTenantInput input, CancellationToken cancellationToken = default);

    /// <summary>Reactivates a suspended tenant (AC-5/FR-5): status → Active, clear suspension fields, notify.</summary>
    Task<Result<TenantLifecycleResultDto>> ReactivateAsync(ReactivateTenantInput input, CancellationToken cancellationToken = default);

    /// <summary>Restores a terminating tenant within grace (AC-6/FR-6): clear scheduled-at, de-queue jobs, notify.</summary>
    Task<Result<TenantLifecycleResultDto>> RestoreAsync(RestoreTenantInput input, CancellationToken cancellationToken = default);

    /// <summary>Lists the lifecycle-event history for a tenant, newest first (System Admin + System Support read).</summary>
    Task<Result<IReadOnlyList<TenantLifecycleEventDto>>> GetLifecycleHistoryAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
