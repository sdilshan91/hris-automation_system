using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveReports.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveReports.Queries;

/// <summary>
/// Exports a leave report to CSV or XLSX (US-LV-012 FR-4, AC-5). Synchronous for &lt;= 5,000 rows;
/// larger exports are enqueued on Hangfire and a job id is returned (FR-5).
/// </summary>
public sealed record ExportLeaveReportQuery(
    LeaveReportType ReportType,
    ReportExportFormat Format,
    LeaveReportQueryParams QueryParams) : IRequest<Result<LeaveReportExportResult>>;
