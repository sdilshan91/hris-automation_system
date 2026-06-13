using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

public sealed class GetMyUpcomingLeavesQueryHandler
    : IRequestHandler<GetMyUpcomingLeavesQuery, Result<IReadOnlyList<UpcomingLeaveDto>>>
{
    private readonly ILeaveDashboardService _service;

    public GetMyUpcomingLeavesQueryHandler(ILeaveDashboardService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<UpcomingLeaveDto>>> Handle(
        GetMyUpcomingLeavesQuery request, CancellationToken cancellationToken)
    {
        return _service.GetMyUpcomingAsync(cancellationToken);
    }
}
