using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Queries;

public sealed class ComputeEffectiveEntitlementQueryHandler
    : IRequestHandler<ComputeEffectiveEntitlementQuery, Result<EffectiveEntitlementDto>>
{
    private readonly ILeaveEntitlementService _service;

    public ComputeEffectiveEntitlementQueryHandler(ILeaveEntitlementService service)
    {
        _service = service;
    }

    public Task<Result<EffectiveEntitlementDto>> Handle(
        ComputeEffectiveEntitlementQuery request, CancellationToken cancellationToken)
    {
        return _service.ComputeEffectiveEntitlementAsync(
            request.EmployeeId, request.LeaveTypeId, request.LeaveYear, cancellationToken);
    }
}
