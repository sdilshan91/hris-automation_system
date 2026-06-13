using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

public sealed class GetPendingLeaveRequestsQueryHandler
    : IRequestHandler<GetPendingLeaveRequestsQuery, Result<PendingLeaveQueueResult>>
{
    private readonly ILeaveRequestService _service;

    public GetPendingLeaveRequestsQueryHandler(ILeaveRequestService service)
    {
        _service = service;
    }

    public Task<Result<PendingLeaveQueueResult>> Handle(
        GetPendingLeaveRequestsQuery request, CancellationToken cancellationToken)
    {
        return _service.GetPendingForManagerAsync(request.Params, cancellationToken);
    }
}
