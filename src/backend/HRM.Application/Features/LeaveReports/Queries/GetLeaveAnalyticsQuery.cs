using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveReports.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveReports.Queries;

/// <summary>
/// Returns aggregated, chart-library-agnostic analytics data for a chart (US-LV-012 FR-7).
/// Role-scoped per the caller (BR-2).
/// </summary>
public sealed record GetLeaveAnalyticsQuery(
    LeaveAnalyticsChartType ChartType,
    LeaveReportQueryParams QueryParams) : IRequest<Result<LeaveAnalyticsResult>>;
