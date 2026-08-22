using HRM.Application.Common.Helpers;
using HRM.Application.DTOs;
using HRM.Application.Features.LeaveReports.DTOs;
using HRM.Application.Features.LeaveReports.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Leave reports and analytics for HR (US-LV-012). All endpoints are gated with
/// <c>Leave.Reports</c> (granted to Tenant Admin / HR Manager / HR Officer / Auditor); the handler
/// applies the BR-2 row-level role scope on top (HR/All → all data, manager → team, employee → self).
///
/// A dedicated controller — following the <see cref="LeaveCarryForwardController"/> /
/// <see cref="LeaveLopController"/> precedent for focused leave sub-resources.
/// </summary>
[ApiController]
[Route("api/v1/leaves")]
[Authorize]
public sealed class LeaveReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveReportsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/leaves/reports/summary
    /// The three landing-page summary cards (US-LV-012): utilization %, top leave type, absenteeism %.
    /// </summary>
    /// <remarks>
    /// Declared BEFORE <c>reports/{reportType}</c>, and that ordering is load-bearing: <c>{reportType}</c> is
    /// an UNCONSTRAINED string route, so it would otherwise capture "summary", fail to parse it as a
    /// <see cref="LeaveReportType"/>, and return 400 "Unknown report type 'summary'". That is exactly what
    /// the frontend has been getting since the dashboard shipped.
    /// </remarks>
    [HttpGet("reports/summary")]
    [RequirePermission("Leave.Reports")]
    [ProducesResponseType(typeof(ApiResponse<LeaveSummaryMetricsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummaryMetrics(
        [FromQuery] LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetLeaveSummaryMetricsQuery(queryParams), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LeaveSummaryMetricsDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/leaves/reports/{reportType}
    /// Generates a pre-built tabular leave report (FR-1, FR-6) with filters (FR-2), server-side sort +
    /// pagination (FR-3). {reportType} is one of: BalanceSummary, Utilization, Absenteeism,
    /// CarryForwardSummary, LopSummary, DepartmentCalendarCoverage (case-insensitive).
    /// </summary>
    [HttpGet("reports/{reportType}")]
    [RequirePermission("Leave.Reports")]
    [ProducesResponseType(typeof(ApiResponse<LeaveReportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetReport(
        [FromRoute] string reportType,
        [FromQuery] LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken)
    {
        if (!EnumParsing.TryParseTolerant<LeaveReportType>(reportType, out var parsed))
            return StatusCode(400, ApiResponse.Fail($"Unknown report type '{reportType}'."));

        var result = await _mediator.Send(new GetLeaveReportQuery(parsed, queryParams), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LeaveReportResult>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/leaves/analytics/{chartType}
    /// Returns aggregated, chart-library-agnostic data for a chart (FR-7). {chartType} is one of:
    /// UtilizationByDepartment, LeaveByType, MonthlyTrend (case-insensitive).
    /// </summary>
    [HttpGet("analytics/{chartType}")]
    [RequirePermission("Leave.Reports")]
    [ProducesResponseType(typeof(ApiResponse<LeaveAnalyticsResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAnalytics(
        [FromRoute] string chartType,
        [FromQuery] LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken)
    {
        if (!EnumParsing.TryParseTolerant<LeaveAnalyticsChartType>(chartType, out var parsed))
            return StatusCode(400, ApiResponse.Fail($"Unknown chart type '{chartType}'."));

        var result = await _mediator.Send(new GetLeaveAnalyticsQuery(parsed, queryParams), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LeaveAnalyticsResult>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/leaves/reports/{reportType}/export?format=csv|xlsx
    /// Exports a report (FR-4, AC-5). For &lt;= 5,000 rows the file is returned inline; for larger
    /// datasets the export is enqueued as a Hangfire background job and a 202 with the job id is
    /// returned (FR-5) — the file is stored via the blob-storage seam and a notification is logged.
    /// </summary>
    [HttpGet("reports/{reportType}/export")]
    [RequirePermission("Leave.Reports")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LeaveReportExportResult>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportReport(
        [FromRoute] string reportType,
        [FromQuery] string format,
        [FromQuery] LeaveReportQueryParams queryParams,
        CancellationToken cancellationToken)
    {
        if (!EnumParsing.TryParseTolerant<LeaveReportType>(reportType, out var parsedType))
            return StatusCode(400, ApiResponse.Fail($"Unknown report type '{reportType}'."));
        if (!EnumParsing.TryParseTolerant<ReportExportFormat>(format, out var parsedFormat))
            return StatusCode(400, ApiResponse.Fail($"Unknown export format '{format}'. Use 'csv' or 'xlsx'."));

        var result = await _mediator.Send(
            new ExportLeaveReportQuery(parsedType, parsedFormat, queryParams), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        var export = result.Value!;

        // FR-5 / AC-5: large export deferred to a background job — return 202 with the job id.
        if (export.Queued)
            return Accepted(ApiResponse<LeaveReportExportResult>.Ok(
                export, "Export is large and is being generated in the background. You will be notified when it is ready."));

        // Synchronous: stream the file.
        return File(export.FileContent!, export.ContentType!, export.FileName!);
    }

}
