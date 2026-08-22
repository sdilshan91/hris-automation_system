using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveReports.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveReports.Queries;

/// <summary>
/// The three landing-page summary cards (US-LV-012): utilization %, top leave type, absenteeism %.
/// Role-scoped per the caller (BR-2), like every other leave report.
/// </summary>
public sealed record GetLeaveSummaryMetricsQuery(LeaveReportQueryParams QueryParams)
    : IRequest<Result<LeaveSummaryMetricsDto>>;

public sealed class GetLeaveSummaryMetricsQueryHandler
    : IRequestHandler<GetLeaveSummaryMetricsQuery, Result<LeaveSummaryMetricsDto>>
{
    private readonly ILeaveReportService _service;

    public GetLeaveSummaryMetricsQueryHandler(ILeaveReportService service) => _service = service;

    public Task<Result<LeaveSummaryMetricsDto>> Handle(
        GetLeaveSummaryMetricsQuery request, CancellationToken cancellationToken)
        => _service.GetSummaryMetricsAsync(request.QueryParams, cancellationToken);
}
