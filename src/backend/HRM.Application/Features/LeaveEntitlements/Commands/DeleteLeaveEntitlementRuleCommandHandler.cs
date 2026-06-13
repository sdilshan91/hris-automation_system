using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Commands;

public sealed class DeleteLeaveEntitlementRuleCommandHandler
    : IRequestHandler<DeleteLeaveEntitlementRuleCommand, Result>
{
    private readonly ILeaveEntitlementService _service;

    public DeleteLeaveEntitlementRuleCommandHandler(ILeaveEntitlementService service)
    {
        _service = service;
    }

    public Task<Result> Handle(
        DeleteLeaveEntitlementRuleCommand request, CancellationToken cancellationToken)
    {
        return _service.DeleteRuleAsync(request.RuleId, cancellationToken);
    }
}
