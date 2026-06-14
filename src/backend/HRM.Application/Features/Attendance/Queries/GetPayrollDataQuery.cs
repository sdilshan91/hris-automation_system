using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// US-ATT-009 FR-1/FR-2: per-employee attendance data for the payroll module for a period, optionally
/// scoped to a set of employee ids.
/// </summary>
public sealed record GetPayrollDataQuery(int Year, int Month, IReadOnlyList<Guid>? EmployeeIds)
    : IRequest<Result<AttendancePayrollResult>>;

public sealed class GetPayrollDataQueryHandler
    : IRequestHandler<GetPayrollDataQuery, Result<AttendancePayrollResult>>
{
    private readonly IAttendancePayrollService _service;

    public GetPayrollDataQueryHandler(IAttendancePayrollService service) => _service = service;

    public Task<Result<AttendancePayrollResult>> Handle(
        GetPayrollDataQuery request, CancellationToken cancellationToken)
        => _service.GetPayrollDataAsync(request.Year, request.Month, request.EmployeeIds, cancellationToken);
}
