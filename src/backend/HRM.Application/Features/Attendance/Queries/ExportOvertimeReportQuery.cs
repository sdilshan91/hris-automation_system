using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// ISSUE-081 (US-ATT-006 §8/AC-5): query to export the monthly overtime report as a CSV file download.
/// </summary>
public sealed record ExportOvertimeReportQuery(
    int Year, int Month, string? Format) : IRequest<Result<OvertimeReportExportResult>>;

public sealed class ExportOvertimeReportQueryHandler
    : IRequestHandler<ExportOvertimeReportQuery, Result<OvertimeReportExportResult>>
{
    private readonly IOvertimeService _service;

    public ExportOvertimeReportQueryHandler(IOvertimeService service) => _service = service;

    public Task<Result<OvertimeReportExportResult>> Handle(
        ExportOvertimeReportQuery request, CancellationToken cancellationToken)
        => _service.ExportMonthlyReportAsync(request.Year, request.Month, request.Format, cancellationToken);
}
