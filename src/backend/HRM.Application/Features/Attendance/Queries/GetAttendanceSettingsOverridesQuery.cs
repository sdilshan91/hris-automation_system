using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// CAL-4b / US-ATT-011 AC-3: lists every Location attendance-policy override configured for the tenant
/// (each with its LocationId + LocationName). The tenant-default row is excluded.
/// </summary>
public sealed record GetAttendanceSettingsOverridesQuery
    : IRequest<Result<IReadOnlyList<AttendanceSettingsDto>>>;

public sealed class GetAttendanceSettingsOverridesQueryHandler
    : IRequestHandler<GetAttendanceSettingsOverridesQuery, Result<IReadOnlyList<AttendanceSettingsDto>>>
{
    private readonly IAttendanceSettingsService _service;

    public GetAttendanceSettingsOverridesQueryHandler(IAttendanceSettingsService service)
    {
        _service = service;
    }

    public Task<Result<IReadOnlyList<AttendanceSettingsDto>>> Handle(
        GetAttendanceSettingsOverridesQuery request, CancellationToken cancellationToken)
        => _service.GetOverridesAsync(cancellationToken);
}
