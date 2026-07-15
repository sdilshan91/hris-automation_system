using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// CAL-4b / US-ATT-011 AC-3: reads ONE Location's attendance-policy override. 404 when the location has
/// no override (its employees fall back to the tenant default); 400 "invalid_location" when the location
/// does not exist in this tenant.
/// </summary>
public sealed record GetLocationAttendanceSettingsQuery(Guid LocationId)
    : IRequest<Result<AttendanceSettingsDto>>;

public sealed class GetLocationAttendanceSettingsQueryHandler
    : IRequestHandler<GetLocationAttendanceSettingsQuery, Result<AttendanceSettingsDto>>
{
    private readonly IAttendanceSettingsService _service;

    public GetLocationAttendanceSettingsQueryHandler(IAttendanceSettingsService service)
    {
        _service = service;
    }

    public Task<Result<AttendanceSettingsDto>> Handle(
        GetLocationAttendanceSettingsQuery request, CancellationToken cancellationToken)
        => _service.GetOverrideAsync(request.LocationId, cancellationToken);
}
