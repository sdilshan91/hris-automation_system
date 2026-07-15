using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// CAL-4b / US-ATT-011 AC-3: reads the TENANT-DEFAULT attendance policy (the row whose LocationId is
/// null), defaulting when none is configured. Never returns a Location override.
/// </summary>
public sealed record GetAttendanceSettingsQuery : IRequest<Result<AttendanceSettingsDto>>;

public sealed class GetAttendanceSettingsQueryHandler
    : IRequestHandler<GetAttendanceSettingsQuery, Result<AttendanceSettingsDto>>
{
    private readonly IAttendanceSettingsService _service;

    public GetAttendanceSettingsQueryHandler(IAttendanceSettingsService service)
    {
        _service = service;
    }

    public Task<Result<AttendanceSettingsDto>> Handle(
        GetAttendanceSettingsQuery request, CancellationToken cancellationToken)
        => _service.GetTenantSettingsAsync(cancellationToken);
}
