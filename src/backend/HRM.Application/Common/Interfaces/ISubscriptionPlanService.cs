using HRM.Application.Common.Models;
using HRM.Application.Features.SubscriptionPlans.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// System-admin subscription-plan management (US-ADM-009). Runs in the system/admin context (no resolved tenant)
/// and operates ACROSS tenants: <see cref="SubscriptionPlan"/> and <see cref="PlanLimitOverride"/> are platform
/// tables (not tenant-scoped). All write actions are audited to the system audit log (NFR-3).
/// </summary>
public interface ISubscriptionPlanService
{
    /// <summary>Lists all plans with their active-tenant count (AC-1/FR-5).</summary>
    Task<Result<IReadOnlyList<PlanListItemDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets full plan detail by id (AC-2/FR-2). 404 when absent.</summary>
    Task<Result<PlanDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a plan (AC-2/FR-2/FR-3). Validates the code is unique + well-formed and the enabled modules are
    /// canonical (FR-6). Audits "Plan.Created".
    /// </summary>
    Task<Result<CreatePlanResultDto>> CreateAsync(
        string code, PlanEditableFields fields, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a plan's editable fields (AC-3). The <c>code</c> is IMMUTABLE — callers never pass it. Audits
    /// before/after. 404 when absent.
    /// </summary>
    Task<Result> UpdateAsync(
        Guid id, PlanEditableFields fields, CancellationToken cancellationToken = default);

    /// <summary>Archives a plan: sets IsActive=false (AC-4). Existing tenants are unaffected. Audits "Plan.Archived".</summary>
    Task<Result> ArchiveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a plan (FR-7/BR-5). REJECTED if any tenant (including terminated) references the plan code. Audits
    /// "Plan.Deleted" on success.
    /// </summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // ── Per-tenant plan-limit overrides (AC-5/FR-4) ──

    /// <summary>Lists a tenant's plan-limit overrides.</summary>
    Task<Result<IReadOnlyList<PlanLimitOverrideDto>>> ListOverridesAsync(
        Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates (upserts) a per-tenant override for a single limit key (AC-5/FR-4). <c>value</c> null =
    /// unlimited override. Audits "Plan.OverrideUpserted".
    /// </summary>
    Task<Result<PlanLimitOverrideDto>> UpsertOverrideAsync(
        Guid tenantId, string limitKey, long? value, DateTime? expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a per-tenant override by id (AC-5). Audits "Plan.OverrideRemoved". 404 when absent.</summary>
    Task<Result> RemoveOverrideAsync(Guid overrideId, CancellationToken cancellationToken = default);
}
