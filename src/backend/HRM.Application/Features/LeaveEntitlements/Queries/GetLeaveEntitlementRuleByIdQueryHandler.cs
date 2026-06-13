using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Queries;

public sealed class GetLeaveEntitlementRuleByIdQueryHandler
    : IRequestHandler<GetLeaveEntitlementRuleByIdQuery, Result<LeaveEntitlementRuleDto>>
{
    private readonly ILeaveEntitlementService _service;

    public GetLeaveEntitlementRuleByIdQueryHandler(ILeaveEntitlementService service)
    {
        _service = service;
    }

    public Task<Result<LeaveEntitlementRuleDto>> Handle(
        GetLeaveEntitlementRuleByIdQuery request, CancellationToken cancellationToken)
    {
        return _service.GetRuleByIdAsync(request.RuleId, cancellationToken);
    }
}
