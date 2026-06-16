using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Tenants.DTOs;
using MediatR;

namespace HRM.Application.Features.Tenants.Queries;

// ── Tenant list (AC-4) ─────────────────────────────────────────────────────

/// <summary>Lists all tenants for the System Admin console (US-ADM-001 AC-4).</summary>
public sealed record ListTenantsQuery : IRequest<Result<IReadOnlyList<TenantListItemDto>>>;

public sealed class ListTenantsQueryHandler
    : IRequestHandler<ListTenantsQuery, Result<IReadOnlyList<TenantListItemDto>>>
{
    private readonly ITenantProvisioningService _service;
    public ListTenantsQueryHandler(ITenantProvisioningService service) => _service = service;

    public Task<Result<IReadOnlyList<TenantListItemDto>>> Handle(
        ListTenantsQuery request, CancellationToken cancellationToken)
        => _service.ListTenantsAsync(cancellationToken);
}

// ── Subdomain availability (FR-2) ──────────────────────────────────────────

/// <summary>Checks subdomain availability for the debounced FE check (US-ADM-001 FR-2).</summary>
public sealed record CheckSubdomainAvailabilityQuery(string Subdomain)
    : IRequest<Result<SubdomainAvailabilityDto>>;

public sealed class CheckSubdomainAvailabilityQueryHandler
    : IRequestHandler<CheckSubdomainAvailabilityQuery, Result<SubdomainAvailabilityDto>>
{
    private readonly ITenantProvisioningService _service;
    public CheckSubdomainAvailabilityQueryHandler(ITenantProvisioningService service) => _service = service;

    public Task<Result<SubdomainAvailabilityDto>> Handle(
        CheckSubdomainAvailabilityQuery request, CancellationToken cancellationToken)
        => _service.CheckSubdomainAvailabilityAsync(request.Subdomain, cancellationToken);
}

// ── Active plans (Create Tenant form dropdown) ─────────────────────────────

/// <summary>Lists active subscription plans selectable on the Create Tenant form (US-ADM-001).</summary>
public sealed record ListActivePlansQuery : IRequest<Result<IReadOnlyList<SubscriptionPlanDto>>>;

public sealed class ListActivePlansQueryHandler
    : IRequestHandler<ListActivePlansQuery, Result<IReadOnlyList<SubscriptionPlanDto>>>
{
    private readonly ITenantProvisioningService _service;
    public ListActivePlansQueryHandler(ITenantProvisioningService service) => _service = service;

    public Task<Result<IReadOnlyList<SubscriptionPlanDto>>> Handle(
        ListActivePlansQuery request, CancellationToken cancellationToken)
        => _service.ListActivePlansAsync(cancellationToken);
}
