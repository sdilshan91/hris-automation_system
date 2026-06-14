using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Queries;

public sealed class GetTeamLeaveCalendarQueryHandler
    : IRequestHandler<GetTeamLeaveCalendarQuery, Result<TeamLeaveCalendarDto>>
{
    private readonly ILeaveRequestService _service;

    public GetTeamLeaveCalendarQueryHandler(ILeaveRequestService service)
    {
        _service = service;
    }

    public Task<Result<TeamLeaveCalendarDto>> Handle(
        GetTeamLeaveCalendarQuery request, CancellationToken cancellationToken)
    {
        return _service.GetTeamLeaveCalendarAsync(request.Params, cancellationToken);
    }
}
