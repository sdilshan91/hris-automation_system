namespace HRM.Application.Features.Tenants.DTOs;

/// <summary>Input for suspending a tenant (US-ADM-004 AC-1/FR-1).</summary>
public sealed record SuspendTenantInput(Guid TenantId, string Reason);

/// <summary>Input for terminating a tenant (US-ADM-004 AC-3/FR-2). GraceDays 7-90 (BR-4).</summary>
public sealed record TerminateTenantInput(Guid TenantId, string Reason, int GraceDays);

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

/// <summary>A single lifecycle-history row for the System Admin timeline (US-ADM-004 — lifecycle history view).</summary>
public sealed record TenantLifecycleEventDto(
    Guid Id,
    Guid TenantId,
    string EventType,
    string? DetailJson,
    DateTime CreatedAt,
    string? CreatedBy);
