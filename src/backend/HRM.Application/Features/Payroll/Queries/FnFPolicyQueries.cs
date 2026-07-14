using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

/// <summary>Lists all of the tenant's F&amp;F policy versions, newest effective-from first (ISSUE-294 Phase 1).</summary>
public sealed record ListFnFPoliciesQuery() : IRequest<Result<IReadOnlyList<FnFPolicyDto>>>;

public sealed class ListFnFPoliciesQueryHandler : IRequestHandler<ListFnFPoliciesQuery, Result<IReadOnlyList<FnFPolicyDto>>>
{
    private readonly IFnFPolicyService _service;
    public ListFnFPoliciesQueryHandler(IFnFPolicyService service) => _service = service;

    public Task<Result<IReadOnlyList<FnFPolicyDto>>> Handle(ListFnFPoliciesQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(cancellationToken);
}

/// <summary>Resolves the F&amp;F policy version in effect on a date (or the code-default) (ISSUE-294 Phase 1).</summary>
public sealed record GetEffectiveFnFPolicyQuery(DateOnly AsOf) : IRequest<Result<FnFPolicyDto>>;

public sealed class GetEffectiveFnFPolicyQueryHandler : IRequestHandler<GetEffectiveFnFPolicyQuery, Result<FnFPolicyDto>>
{
    private readonly IFnFPolicyService _service;
    public GetEffectiveFnFPolicyQueryHandler(IFnFPolicyService service) => _service = service;

    public Task<Result<FnFPolicyDto>> Handle(GetEffectiveFnFPolicyQuery request, CancellationToken cancellationToken)
        => _service.GetEffectiveAsync(request.AsOf, cancellationToken);
}
