using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// CAL-4b / US-ATT-011 AC-3: upserts ONE Location's attendance-policy override. FULL REPLACE of that
/// scope — see <see cref="AttendanceSettingsDto"/>. The scope comes from <see cref="LocationId"/> (the
/// route), never from the body; the override is the complete policy the admin sent and is NOT seeded from
/// the tenant-default row.
/// </summary>
public sealed record UpsertLocationAttendanceSettingsCommand(Guid LocationId, AttendanceSettingsDto Settings)
    : IRequest<Result<AttendanceSettingsDto>>;

public sealed class UpsertLocationAttendanceSettingsCommandHandler
    : IRequestHandler<UpsertLocationAttendanceSettingsCommand, Result<AttendanceSettingsDto>>
{
    private readonly IAttendanceSettingsService _service;

    public UpsertLocationAttendanceSettingsCommandHandler(IAttendanceSettingsService service)
    {
        _service = service;
    }

    public Task<Result<AttendanceSettingsDto>> Handle(
        UpsertLocationAttendanceSettingsCommand request, CancellationToken cancellationToken)
        => _service.UpsertOverrideAsync(request.LocationId, request.Settings, cancellationToken);
}
