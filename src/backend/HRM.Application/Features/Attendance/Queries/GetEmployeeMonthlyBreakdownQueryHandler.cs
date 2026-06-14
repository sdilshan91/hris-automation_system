using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

public sealed class GetEmployeeMonthlyBreakdownQueryHandler
    : IRequestHandler<GetEmployeeMonthlyBreakdownQuery, Result<EmployeeDailyBreakdownResult>>
{
    private readonly IAttendanceSummaryService _service;

    public GetEmployeeMonthlyBreakdownQueryHandler(IAttendanceSummaryService service)
    {
        _service = service;
    }

    public Task<Result<EmployeeDailyBreakdownResult>> Handle(
        GetEmployeeMonthlyBreakdownQuery request, CancellationToken cancellationToken)
        => _service.GetEmployeeBreakdownAsync(
            request.EmployeeId, request.Year, request.Month, cancellationToken);
}
