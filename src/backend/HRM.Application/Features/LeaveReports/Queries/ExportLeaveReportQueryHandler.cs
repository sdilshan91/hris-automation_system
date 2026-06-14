using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveReports.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveReports.Queries;

public sealed class ExportLeaveReportQueryHandler
    : IRequestHandler<ExportLeaveReportQuery, Result<LeaveReportExportResult>>
{
    private readonly ILeaveReportService _reportService;

    public ExportLeaveReportQueryHandler(ILeaveReportService reportService)
    {
        _reportService = reportService;
    }

    public Task<Result<LeaveReportExportResult>> Handle(
        ExportLeaveReportQuery request, CancellationToken cancellationToken)
        => _reportService.ExportReportAsync(
            request.ReportType, request.Format, request.QueryParams, cancellationToken);
}
