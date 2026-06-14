using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// US-ATT-009 AC-5/FR-4: unlock a previously-locked attendance period.
/// </summary>
public sealed record UnlockPeriodCommand(Guid LockId) : IRequest<Result<PeriodLockDto>>;

public sealed class UnlockPeriodCommandHandler : IRequestHandler<UnlockPeriodCommand, Result<PeriodLockDto>>
{
    private readonly IAttendancePayrollService _service;

    public UnlockPeriodCommandHandler(IAttendancePayrollService service) => _service = service;

    public Task<Result<PeriodLockDto>> Handle(UnlockPeriodCommand request, CancellationToken cancellationToken)
        => _service.UnlockPeriodAsync(request.LockId, cancellationToken);
}
