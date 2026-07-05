using HRM.Application.Common.Models;
using HRM.Application.Features.Tenants.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// System-admin tenant provisioning (US-ADM-001). Runs in the system/admin context (no resolved tenant) and
/// operates ACROSS tenants: it creates the <c>Tenant</c>, creates-or-links the global owner <c>User</c>, the
/// <c>UserTenant</c> + <c>UserTenantRole</c> (Tenant Owner), seeds default master data, writes the
/// <c>TenantLifecycleEvent</c>("created") + structured <c>AuditLog</c>, and dispatches the welcome email — all
/// transactionally and idempotently on subdomain (NFR-2).
/// </summary>
public interface ITenantProvisioningService
{
    /// <summary>Provisions a new tenant (AC-1). Fails (never duplicates) if the subdomain is already taken (NFR-2/BR-2).</summary>
    Task<Result<ProvisionTenantResultDto>> ProvisionAsync(
        ProvisionTenantInput input, CancellationToken cancellationToken = default);

    /// <summary>Lists all tenants for the System Admin console (AC-4), newest first. Optional
    /// case-insensitive <paramref name="search"/> filters on tenant name/subdomain (US-ADM-002 AC-2).</summary>
    Task<Result<IReadOnlyList<TenantListItemDto>>> ListTenantsAsync(
        string? search = null, CancellationToken cancellationToken = default);

    /// <summary>Checks whether a subdomain is available (FR-2): not reserved, valid format, not already taken.</summary>
    Task<Result<SubdomainAvailabilityDto>> CheckSubdomainAvailabilityAsync(
        string subdomain, CancellationToken cancellationToken = default);

    /// <summary>Lists the active subscription plans selectable on the Create Tenant form.</summary>
    Task<Result<IReadOnlyList<SubscriptionPlanDto>>> ListActivePlansAsync(CancellationToken cancellationToken = default);
}
