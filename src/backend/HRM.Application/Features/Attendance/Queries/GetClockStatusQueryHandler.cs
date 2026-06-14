using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

public sealed class GetClockStatusQueryHandler
    : IRequestHandler<GetClockStatusQuery, Result<ClockStatusDto>>
{
    private readonly IAttendanceService _service;

    public GetClockStatusQueryHandler(IAttendanceService service)
    {
        _service = service;
    }

    public Task<Result<ClockStatusDto>> Handle(
        GetClockStatusQuery request, CancellationToken cancellationToken)
    {
        return _service.GetClockStatusAsync(cancellationToken);
    }
}
