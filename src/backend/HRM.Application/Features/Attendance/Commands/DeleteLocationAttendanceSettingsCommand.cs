using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// CAL-4b / US-ATT-011 AC-3: removes ONE Location's attendance-policy override; that Location's employees
/// then fall back to the tenant-default policy (row-level resolution).
/// </summary>
public sealed record DeleteLocationAttendanceSettingsCommand(Guid LocationId) : IRequest<Result>;

public sealed class DeleteLocationAttendanceSettingsCommandHandler
    : IRequestHandler<DeleteLocationAttendanceSettingsCommand, Result>
{
    private readonly IAttendanceSettingsService _service;

    public DeleteLocationAttendanceSettingsCommandHandler(IAttendanceSettingsService service)
    {
        _service = service;
    }

    public Task<Result> Handle(
        DeleteLocationAttendanceSettingsCommand request, CancellationToken cancellationToken)
        => _service.DeleteOverrideAsync(request.LocationId, cancellationToken);
}
