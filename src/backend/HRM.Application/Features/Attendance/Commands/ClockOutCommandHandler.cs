using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

public sealed class ClockOutCommandHandler
    : IRequestHandler<ClockOutCommand, Result<ClockOutResultDto>>
{
    private readonly IAttendanceService _service;

    public ClockOutCommandHandler(IAttendanceService service)
    {
        _service = service;
    }

    public Task<Result<ClockOutResultDto>> Handle(
        ClockOutCommand request, CancellationToken cancellationToken)
    {
        return _service.ClockOutAsync(new ClockOutData
        {
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IpAddress = request.IpAddress,
            UserAgent = request.UserAgent,
        }, cancellationToken);
    }
}
