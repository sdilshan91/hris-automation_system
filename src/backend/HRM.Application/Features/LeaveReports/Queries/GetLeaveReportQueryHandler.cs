using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveReports.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveReports.Queries;

public sealed class GetLeaveReportQueryHandler
    : IRequestHandler<GetLeaveReportQuery, Result<LeaveReportResult>>
{
    private readonly ILeaveReportService _reportService;

    public GetLeaveReportQueryHandler(ILeaveReportService reportService)
    {
        _reportService = reportService;
    }

    public Task<Result<LeaveReportResult>> Handle(
        GetLeaveReportQuery request, CancellationToken cancellationToken)
        => _reportService.GenerateReportAsync(request.ReportType, request.QueryParams, cancellationToken);
}
