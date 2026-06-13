using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

public sealed class GetMyLeaveRequestsQueryHandler
    : IRequestHandler<GetMyLeaveRequestsQuery, Result<IReadOnlyList<LeaveRequestDto>>>
{
    private readonly ILeaveRequestService _service;

    public GetMyLeaveRequestsQueryHandler(ILeaveRequestService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<LeaveRequestDto>>> Handle(
        GetMyLeaveRequestsQuery request, CancellationToken cancellationToken)
    {
        return _service.GetMineAsync(cancellationToken);
    }
}
