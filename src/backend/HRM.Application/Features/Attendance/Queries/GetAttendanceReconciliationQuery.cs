using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// US-ATT-009 FR-5: attendance-side reconciliation rows for a period (payroll-input columns deferred).
/// </summary>
public sealed record GetAttendanceReconciliationQuery(int Year, int Month)
    : IRequest<Result<ReconciliationResult>>;

public sealed class GetAttendanceReconciliationQueryHandler
    : IRequestHandler<GetAttendanceReconciliationQuery, Result<ReconciliationResult>>
{
    private readonly IAttendancePayrollService _service;

    public GetAttendanceReconciliationQueryHandler(IAttendancePayrollService service) => _service = service;

    public Task<Result<ReconciliationResult>> Handle(
        GetAttendanceReconciliationQuery request, CancellationToken cancellationToken)
        => _service.GetReconciliationAsync(request.Year, request.Month, cancellationToken);
}
