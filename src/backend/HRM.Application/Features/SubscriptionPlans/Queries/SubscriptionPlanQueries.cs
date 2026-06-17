using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.SubscriptionPlans.DTOs;
using MediatR;

namespace HRM.Application.Features.SubscriptionPlans.Queries;

// ── List plans (AC-1/FR-5) ────────────────────────────────────────────────────

/// <summary>Lists all plans with active-tenant counts (US-ADM-009 AC-1/FR-5).</summary>
public sealed record ListPlansQuery : IRequest<Result<IReadOnlyList<PlanListItemDto>>>;

public sealed class ListPlansQueryHandler
    : IRequestHandler<ListPlansQuery, Result<IReadOnlyList<PlanListItemDto>>>
{
    private readonly ISubscriptionPlanService _service;
    public ListPlansQueryHandler(ISubscriptionPlanService service) => _service = service;

    public Task<Result<IReadOnlyList<PlanListItemDto>>> Handle(
        ListPlansQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(cancellationToken);
}

// ── Get plan detail (AC-2/FR-2) ───────────────────────────────────────────────

/// <summary>Gets full plan detail by id (US-ADM-009 AC-2/FR-2).</summary>
public sealed record GetPlanQuery(Guid Id) : IRequest<Result<PlanDetailDto>>;

public sealed class GetPlanQueryHandler : IRequestHandler<GetPlanQuery, Result<PlanDetailDto>>
{
    private readonly ISubscriptionPlanService _service;
    public GetPlanQueryHandler(ISubscriptionPlanService service) => _service = service;

    public Task<Result<PlanDetailDto>> Handle(GetPlanQuery request, CancellationToken cancellationToken)
        => _service.GetAsync(request.Id, cancellationToken);
}

// ── List per-tenant overrides (AC-5/FR-4) ─────────────────────────────────────

/// <summary>Lists a tenant's plan-limit overrides (US-ADM-009 AC-5/FR-4).</summary>
public sealed record ListPlanLimitOverridesQuery(Guid TenantId)
    : IRequest<Result<IReadOnlyList<PlanLimitOverrideDto>>>;

public sealed class ListPlanLimitOverridesQueryHandler
    : IRequestHandler<ListPlanLimitOverridesQuery, Result<IReadOnlyList<PlanLimitOverrideDto>>>
{
    private readonly ISubscriptionPlanService _service;
    public ListPlanLimitOverridesQueryHandler(ISubscriptionPlanService service) => _service = service;

    public Task<Result<IReadOnlyList<PlanLimitOverrideDto>>> Handle(
        ListPlanLimitOverridesQuery request, CancellationToken cancellationToken)
        => _service.ListOverridesAsync(request.TenantId, cancellationToken);
}
