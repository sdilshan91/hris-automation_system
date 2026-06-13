using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Queries;

public sealed class GetLeaveEntitlementOverridesQueryHandler
    : IRequestHandler<GetLeaveEntitlementOverridesQuery, Result<IReadOnlyList<LeaveEntitlementOverrideDto>>>
{
    private readonly ILeaveEntitlementService _service;

    public GetLeaveEntitlementOverridesQueryHandler(ILeaveEntitlementService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<LeaveEntitlementOverrideDto>>> Handle(
        GetLeaveEntitlementOverridesQuery request, CancellationToken cancellationToken)
    {
        return _service.GetOverridesAsync(request.EmployeeId, request.LeaveTypeId, request.LeaveYear, cancellationToken);
    }
}
