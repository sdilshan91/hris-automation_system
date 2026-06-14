using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

public sealed class GetOvertimeReportQueryHandler
    : IRequestHandler<GetOvertimeReportQuery, Result<OvertimeReportResult>>
{
    private readonly IOvertimeService _service;

    public GetOvertimeReportQueryHandler(IOvertimeService service)
    {
        _service = service;
    }

    public Task<Result<OvertimeReportResult>> Handle(
        GetOvertimeReportQuery request, CancellationToken cancellationToken)
    {
        return _service.GetMonthlyReportAsync(request.Year, request.Month, cancellationToken);
    }
}
