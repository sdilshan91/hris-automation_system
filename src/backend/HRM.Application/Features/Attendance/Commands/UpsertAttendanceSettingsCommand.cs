using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// CAL-4b / US-ATT-011 AC-3: upserts the TENANT-DEFAULT attendance policy (the row whose LocationId is
/// null). FULL REPLACE of that scope — see <see cref="AttendanceSettingsDto"/>. Never touches a Location
/// override.
/// </summary>
public sealed record UpsertAttendanceSettingsCommand(AttendanceSettingsDto Settings)
    : IRequest<Result<AttendanceSettingsDto>>;

public sealed class UpsertAttendanceSettingsCommandHandler
    : IRequestHandler<UpsertAttendanceSettingsCommand, Result<AttendanceSettingsDto>>
{
    private readonly IAttendanceSettingsService _service;

    public UpsertAttendanceSettingsCommandHandler(IAttendanceSettingsService service)
    {
        _service = service;
    }

    public Task<Result<AttendanceSettingsDto>> Handle(
        UpsertAttendanceSettingsCommand request, CancellationToken cancellationToken)
        => _service.UpsertTenantSettingsAsync(request.Settings, cancellationToken);
}
