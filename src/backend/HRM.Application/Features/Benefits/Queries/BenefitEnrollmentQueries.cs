using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Benefits.DTOs;
using MediatR;

namespace HRM.Application.Features.Benefits.Queries;

// ── List eligibility rules for a plan (AC-1) ─────────────────────────────────
public sealed record GetEligibilityRulesQuery(Guid PlanId) : IRequest<Result<IReadOnlyList<EligibilityRuleDto>>>;

public sealed class GetEligibilityRulesQueryHandler : IRequestHandler<GetEligibilityRulesQuery, Result<IReadOnlyList<EligibilityRuleDto>>>
{
    private readonly IBenefitEnrollmentService _service;
    public GetEligibilityRulesQueryHandler(IBenefitEnrollmentService service) => _service = service;

    public Task<Result<IReadOnlyList<EligibilityRuleDto>>> Handle(GetEligibilityRulesQuery request, CancellationToken cancellationToken)
        => _service.GetRulesAsync(request.PlanId, cancellationToken);
}

// ── Current user's eligible plans (AC-2/AC-8) ────────────────────────────────
public sealed record GetMyEligiblePlansQuery : IRequest<Result<IReadOnlyList<EligiblePlanDto>>>;

public sealed class GetMyEligiblePlansQueryHandler : IRequestHandler<GetMyEligiblePlansQuery, Result<IReadOnlyList<EligiblePlanDto>>>
{
    private readonly IBenefitEnrollmentService _service;
    public GetMyEligiblePlansQueryHandler(IBenefitEnrollmentService service) => _service = service;

    public Task<Result<IReadOnlyList<EligiblePlanDto>>> Handle(GetMyEligiblePlansQuery request, CancellationToken cancellationToken)
        => _service.GetMyEligiblePlansAsync(cancellationToken);
}

// ── A given employee's eligible plans (AC-8) ─────────────────────────────────
public sealed record GetEmployeeEligiblePlansQuery(Guid EmployeeId) : IRequest<Result<IReadOnlyList<EligiblePlanDto>>>;

public sealed class GetEmployeeEligiblePlansQueryHandler : IRequestHandler<GetEmployeeEligiblePlansQuery, Result<IReadOnlyList<EligiblePlanDto>>>
{
    private readonly IBenefitEnrollmentService _service;
    public GetEmployeeEligiblePlansQueryHandler(IBenefitEnrollmentService service) => _service = service;

    public Task<Result<IReadOnlyList<EligiblePlanDto>>> Handle(GetEmployeeEligiblePlansQuery request, CancellationToken cancellationToken)
        => _service.GetEligiblePlansAsync(request.EmployeeId, cancellationToken);
}

// ── Current user's enrollments (AC-8) ────────────────────────────────────────
public sealed record GetMyEnrollmentsQuery : IRequest<Result<IReadOnlyList<BenefitEnrollmentDto>>>;

public sealed class GetMyEnrollmentsQueryHandler : IRequestHandler<GetMyEnrollmentsQuery, Result<IReadOnlyList<BenefitEnrollmentDto>>>
{
    private readonly IBenefitEnrollmentService _service;
    public GetMyEnrollmentsQueryHandler(IBenefitEnrollmentService service) => _service = service;

    public Task<Result<IReadOnlyList<BenefitEnrollmentDto>>> Handle(GetMyEnrollmentsQuery request, CancellationToken cancellationToken)
        => _service.GetMyEnrollmentsAsync(cancellationToken);
}

// ── A given employee's enrollments (AC-8) ────────────────────────────────────
public sealed record GetEmployeeEnrollmentsQuery(Guid EmployeeId) : IRequest<Result<IReadOnlyList<BenefitEnrollmentDto>>>;

public sealed class GetEmployeeEnrollmentsQueryHandler : IRequestHandler<GetEmployeeEnrollmentsQuery, Result<IReadOnlyList<BenefitEnrollmentDto>>>
{
    private readonly IBenefitEnrollmentService _service;
    public GetEmployeeEnrollmentsQueryHandler(IBenefitEnrollmentService service) => _service = service;

    public Task<Result<IReadOnlyList<BenefitEnrollmentDto>>> Handle(GetEmployeeEnrollmentsQuery request, CancellationToken cancellationToken)
        => _service.GetEmployeeEnrollmentsAsync(request.EmployeeId, cancellationToken);
}
