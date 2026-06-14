using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveReports.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveReports.Queries;

public sealed class GetLeaveAnalyticsQueryHandler
    : IRequestHandler<GetLeaveAnalyticsQuery, Result<LeaveAnalyticsResult>>
{
    private readonly ILeaveReportService _reportService;

    public GetLeaveAnalyticsQueryHandler(ILeaveReportService reportService)
    {
        _reportService = reportService;
    }

    public Task<Result<LeaveAnalyticsResult>> Handle(
        GetLeaveAnalyticsQuery request, CancellationToken cancellationToken)
        => _reportService.GetAnalyticsAsync(request.ChartType, request.QueryParams, cancellationToken);
}
