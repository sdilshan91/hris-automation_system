using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// US-ATT-009 AC-4/FR-3/FR-4: lock an attendance period (inclusive date range).
/// </summary>
public sealed record LockPeriodCommand(DateOnly PeriodStart, DateOnly PeriodEnd)
    : IRequest<Result<PeriodLockDto>>;

public sealed class LockPeriodCommandHandler : IRequestHandler<LockPeriodCommand, Result<PeriodLockDto>>
{
    private readonly IAttendancePayrollService _service;

    public LockPeriodCommandHandler(IAttendancePayrollService service) => _service = service;

    public Task<Result<PeriodLockDto>> Handle(LockPeriodCommand request, CancellationToken cancellationToken)
        => _service.LockPeriodAsync(request.PeriodStart, request.PeriodEnd, cancellationToken);
}
