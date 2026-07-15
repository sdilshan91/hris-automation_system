using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using MediatR;

namespace HRM.Application.Features.Payroll.Queries;

/// <summary>Lists all of the tenant's payroll calendar policy versions, newest effective-from first (CAL-5).</summary>
public sealed record ListPayrollCalendarPoliciesQuery() : IRequest<Result<IReadOnlyList<PayrollCalendarPolicyDto>>>;

public sealed class ListPayrollCalendarPoliciesQueryHandler
    : IRequestHandler<ListPayrollCalendarPoliciesQuery, Result<IReadOnlyList<PayrollCalendarPolicyDto>>>
{
    private readonly IPayrollCalendarPolicyService _service;
    public ListPayrollCalendarPoliciesQueryHandler(IPayrollCalendarPolicyService service) => _service = service;

    public Task<Result<IReadOnlyList<PayrollCalendarPolicyDto>>> Handle(ListPayrollCalendarPoliciesQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(cancellationToken);
}

/// <summary>Resolves the payroll calendar policy in effect on a date (or the code-default) (CAL-5).</summary>
public sealed record GetEffectivePayrollCalendarPolicyQuery(DateOnly AsOf) : IRequest<Result<PayrollCalendarPolicyDto>>;

public sealed class GetEffectivePayrollCalendarPolicyQueryHandler
    : IRequestHandler<GetEffectivePayrollCalendarPolicyQuery, Result<PayrollCalendarPolicyDto>>
{
    private readonly IPayrollCalendarPolicyService _service;
    public GetEffectivePayrollCalendarPolicyQueryHandler(IPayrollCalendarPolicyService service) => _service = service;

    public Task<Result<PayrollCalendarPolicyDto>> Handle(GetEffectivePayrollCalendarPolicyQuery request, CancellationToken cancellationToken)
        => _service.GetEffectiveAsync(request.AsOf, cancellationToken);
}
