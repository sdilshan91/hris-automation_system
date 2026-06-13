using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Commands;

public sealed class BulkCreateRulesCommandHandler
    : IRequestHandler<BulkCreateRulesCommand, Result<IReadOnlyList<LeaveEntitlementRuleDto>>>
{
    private readonly ILeaveEntitlementService _service;

    public BulkCreateRulesCommandHandler(ILeaveEntitlementService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<LeaveEntitlementRuleDto>>> Handle(
        BulkCreateRulesCommand request, CancellationToken cancellationToken)
    {
        return _service.BulkCreateRulesAsync(request.Rules, cancellationToken);
    }
}
