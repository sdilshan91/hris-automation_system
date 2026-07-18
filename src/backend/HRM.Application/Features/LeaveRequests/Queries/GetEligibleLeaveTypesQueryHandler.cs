using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveTypes.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

public sealed class GetEligibleLeaveTypesQueryHandler
    : IRequestHandler<GetEligibleLeaveTypesQuery, Result<IReadOnlyList<LeaveTypeDto>>>
{
    private readonly ILeaveRequestService _service;

    public GetEligibleLeaveTypesQueryHandler(ILeaveRequestService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<LeaveTypeDto>>> Handle(
        GetEligibleLeaveTypesQuery request, CancellationToken cancellationToken)
    {
        return _service.GetEligibleLeaveTypesAsync(cancellationToken);
    }
}
