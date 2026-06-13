using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Commands;

public sealed class DeleteLeaveEntitlementOverrideCommandHandler
    : IRequestHandler<DeleteLeaveEntitlementOverrideCommand, Result>
{
    private readonly ILeaveEntitlementService _service;

    public DeleteLeaveEntitlementOverrideCommandHandler(ILeaveEntitlementService service)
    {
        _service = service;
    }

    public Task<Result> Handle(
        DeleteLeaveEntitlementOverrideCommand request, CancellationToken cancellationToken)
    {
        return _service.DeleteOverrideAsync(request.OverrideId, cancellationToken);
    }
}
