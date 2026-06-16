namespace HRM.Application.Features.Tenants.DTOs;

/// <summary>
/// Input for provisioning a new tenant (US-ADM-001 FR-1). <c>TrialDays</c> is optional — when null the
/// chosen plan's default trial length is used.
/// </summary>
public sealed record ProvisionTenantInput(
    string Name,
    string Subdomain,
    Guid SubscriptionPlanId,
    string OwnerEmail,
    string Region,
    int? TrialDays);

/// <summary>Result of a successful provision (US-ADM-001 §7 Output).</summary>
public sealed record ProvisionTenantResultDto(
    Guid TenantId,
    string Subdomain,
    string Status,
    DateTime CreatedAt,
    bool OwnerLinked);

/// <summary>A tenant row in the System Admin tenant list (US-ADM-001 AC-4).</summary>
public sealed record TenantListItemDto(
    Guid Id,
    string Name,
    string Subdomain,
    string Status,
    string Plan,
    DateTime CreatedAt);

/// <summary>Subdomain availability check result for the debounced FE check (US-ADM-001 FR-2).</summary>
public sealed record SubdomainAvailabilityDto(
    bool Available,
    string? Reason);

/// <summary>A selectable subscription plan (US-ADM-001 — plan dropdown source for the Create Tenant form).</summary>
public sealed record SubscriptionPlanDto(
    Guid Id,
    string Name,
    string Code,
    decimal PriceMonthly,
    int TrialDays,
    int? MaxEmployees);
