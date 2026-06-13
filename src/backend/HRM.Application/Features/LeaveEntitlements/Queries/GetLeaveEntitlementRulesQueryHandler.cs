using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Queries;

public sealed class GetLeaveEntitlementRulesQueryHandler
    : IRequestHandler<GetLeaveEntitlementRulesQuery, Result<IReadOnlyList<LeaveEntitlementRuleDto>>>
{
    private readonly ILeaveEntitlementService _service;

    public GetLeaveEntitlementRulesQueryHandler(ILeaveEntitlementService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<LeaveEntitlementRuleDto>>> Handle(
        GetLeaveEntitlementRulesQuery request, CancellationToken cancellationToken)
    {
        return _service.GetRulesAsync(request.LeaveTypeId, cancellationToken);
    }
}
