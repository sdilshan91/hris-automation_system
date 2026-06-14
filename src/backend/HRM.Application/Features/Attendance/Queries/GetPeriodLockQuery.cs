using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// US-ATT-009 §7: the current ACTIVE attendance-period lock covering a month (null when not locked).
/// </summary>
public sealed record GetPeriodLockQuery(int Year, int Month) : IRequest<Result<PeriodLockDto?>>;

public sealed class GetPeriodLockQueryHandler
    : IRequestHandler<GetPeriodLockQuery, Result<PeriodLockDto?>>
{
    private readonly IAttendancePayrollService _service;

    public GetPeriodLockQueryHandler(IAttendancePayrollService service) => _service = service;

    public Task<Result<PeriodLockDto?>> Handle(
        GetPeriodLockQuery request, CancellationToken cancellationToken)
        => _service.GetPeriodLockAsync(request.Year, request.Month, cancellationToken);
}
