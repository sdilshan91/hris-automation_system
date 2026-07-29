namespace HRM.Application.Features.Tenants.DTOs;

/// <summary>Input for suspending a tenant (US-ADM-004 AC-1/FR-1).</summary>
public sealed record SuspendTenantInput(Guid TenantId, string Reason);

/// <summary>
/// Input for terminating a tenant (US-ADM-004 AC-3/FR-2). <c>GraceDays</c> null ⇒ apply the plan default
/// (BUG-002); a supplied value is range-checked 7-90 (BR-4).
/// </summary>
public sealed record TerminateTenantInput(Guid TenantId, string Reason, int? GraceDays);

/// <summary>Input for reactivating a suspended tenant (US-ADM-004 AC-5/FR-5).</summary>
public sealed record ReactivateTenantInput(Guid TenantId);

/// <summary>Input for restoring a terminating tenant within grace (US-ADM-004 AC-6/FR-6).</summary>
public sealed record RestoreTenantInput(Guid TenantId);

/// <summary>
/// Result of a lifecycle transition (US-ADM-004 §7 Output). Carries the tenant's new state and the
/// lifecycle-relevant timestamps so the FE can refresh the detail view without a second round-trip.
/// </summary>
public sealed record TenantLifecycleResultDto(
    Guid TenantId,
    string Subdomain,
    string Status,
    DateTime? SuspendedAt,
    string? SuspendedReason,
    DateTime? TerminationScheduledAt,
    string EventType);

/// <summary>Input for moving an existing tenant onto a different subscription plan (ISSUE-341).</summary>
public sealed record ChangeTenantPlanInput(Guid TenantId, string PlanCode);

/// <summary>
/// Result of a tenant plan change (ISSUE-341). Returns the recomputed entitlement snapshot so the System Admin
/// UI can reflect the tenant's new modules without a second round-trip.
/// </summary>
public sealed record ChangeTenantPlanResultDto(
    Guid TenantId,
    string Subdomain,
    string PlanCode,
    IReadOnlyList<string> EnabledModules);

/// <summary>A single lifecycle-history row for the System Admin timeline (US-ADM-004 — lifecycle history view).</summary>
public sealed record TenantLifecycleEventDto(
    Guid Id,
    Guid TenantId,
    string EventType,
    string? DetailJson,
    DateTime CreatedAt,
    string? CreatedBy);
