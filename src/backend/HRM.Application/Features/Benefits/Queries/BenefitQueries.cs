using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Benefits.DTOs;
using MediatR;

namespace HRM.Application.Features.Benefits.Queries;

// ── List plans (AC-5) ────────────────────────────────────────────────────────
public sealed record GetBenefitPlansQuery : IRequest<Result<IReadOnlyList<BenefitPlanDto>>>;

public sealed class GetBenefitPlansQueryHandler : IRequestHandler<GetBenefitPlansQuery, Result<IReadOnlyList<BenefitPlanDto>>>
{
    private readonly IBenefitPlanService _service;
    public GetBenefitPlansQueryHandler(IBenefitPlanService service) => _service = service;

    public Task<Result<IReadOnlyList<BenefitPlanDto>>> Handle(GetBenefitPlansQuery request, CancellationToken cancellationToken)
        => _service.GetPlansAsync(cancellationToken);
}

// ── Get one plan (AC-5) ──────────────────────────────────────────────────────
public sealed record GetBenefitPlanByIdQuery(Guid PlanId) : IRequest<Result<BenefitPlanDto>>;

public sealed class GetBenefitPlanByIdQueryHandler : IRequestHandler<GetBenefitPlanByIdQuery, Result<BenefitPlanDto>>
{
    private readonly IBenefitPlanService _service;
    public GetBenefitPlanByIdQueryHandler(IBenefitPlanService service) => _service = service;

    public Task<Result<BenefitPlanDto>> Handle(GetBenefitPlanByIdQuery request, CancellationToken cancellationToken)
        => _service.GetPlanByIdAsync(request.PlanId, cancellationToken);
}
