using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Commands;

public sealed class UpsertLeaveEntitlementOverrideCommandHandler
    : IRequestHandler<UpsertLeaveEntitlementOverrideCommand, Result<LeaveEntitlementOverrideDto>>
{
    private readonly ILeaveEntitlementService _service;

    public UpsertLeaveEntitlementOverrideCommandHandler(ILeaveEntitlementService service)
    {
        _service = service;
    }

    public Task<Result<LeaveEntitlementOverrideDto>> Handle(
        UpsertLeaveEntitlementOverrideCommand request, CancellationToken cancellationToken)
    {
        return _service.UpsertOverrideAsync(new UpsertLeaveEntitlementOverrideRequest
        {
            EmployeeId = request.EmployeeId,
            LeaveTypeId = request.LeaveTypeId,
            LeaveYear = request.LeaveYear,
            EntitlementDays = request.EntitlementDays,
            Reason = request.Reason,
        }, cancellationToken);
    }
}
