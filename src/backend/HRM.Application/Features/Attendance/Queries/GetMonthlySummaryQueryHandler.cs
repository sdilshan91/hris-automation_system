using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

public sealed class GetMonthlySummaryQueryHandler
    : IRequestHandler<GetMonthlySummaryQuery, Result<MonthlySummaryResult>>
{
    private readonly IAttendanceSummaryService _service;

    public GetMonthlySummaryQueryHandler(IAttendanceSummaryService service)
    {
        _service = service;
    }

    public Task<Result<MonthlySummaryResult>> Handle(
        GetMonthlySummaryQuery request, CancellationToken cancellationToken)
        => _service.GetMonthlyAsync(request.Year, request.Month, request.Filter, cancellationToken);
}
