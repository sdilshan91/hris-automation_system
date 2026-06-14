using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

public sealed class ExportMonthlySummaryQueryHandler
    : IRequestHandler<ExportMonthlySummaryQuery, Result<MonthlySummaryExportResult>>
{
    private readonly IAttendanceSummaryService _service;

    public ExportMonthlySummaryQueryHandler(IAttendanceSummaryService service)
    {
        _service = service;
    }

    public Task<Result<MonthlySummaryExportResult>> Handle(
        ExportMonthlySummaryQuery request, CancellationToken cancellationToken)
        => _service.ExportAsync(
            request.Year, request.Month, request.Format, request.Filter, cancellationToken);
}
