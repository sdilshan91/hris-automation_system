using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

public sealed class ClockInCommandHandler
    : IRequestHandler<ClockInCommand, Result<AttendanceLogDto>>
{
    private readonly IAttendanceService _service;

    public ClockInCommandHandler(IAttendanceService service)
    {
        _service = service;
    }

    public Task<Result<AttendanceLogDto>> Handle(
        ClockInCommand request, CancellationToken cancellationToken)
    {
        return _service.ClockInAsync(new ClockInData
        {
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            PhotoUrl = request.PhotoUrl,
            Source = request.Source,
            IpAddress = request.IpAddress,
            UserAgent = request.UserAgent,
            IdempotencyKey = request.IdempotencyKey,
        }, cancellationToken);
    }
}
