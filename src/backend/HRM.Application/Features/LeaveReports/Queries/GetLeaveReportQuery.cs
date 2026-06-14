using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveReports.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveReports.Queries;

/// <summary>
/// Generates a pre-built leave report (US-LV-012 FR-6) with filters, sort and paging (FR-2, FR-3),
/// role-scoped per the caller (BR-2).
/// </summary>
public sealed record GetLeaveReportQuery(
    LeaveReportType ReportType,
    LeaveReportQueryParams QueryParams) : IRequest<Result<LeaveReportResult>>;
