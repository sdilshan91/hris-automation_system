using HRM.Application.DTOs;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Application.Features.Payroll.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Payroll reports and analytics for HR / Tenant Admin (US-PAY-009). All endpoints are gated with the
/// registered <c>Payroll.Export</c> permission (held by Tenant Admin + HR Officer, the report consumers).
/// Reports read over the existing payroll_slip / payroll_slip_detail / payroll_adjustment data from
/// FINALIZED runs only (BR-1) and are tenant-scoped via the EF global query filter (AC-5 / FR-8).
///
/// <para>A dedicated controller — following the <see cref="LeaveReportsController"/> precedent for a
/// focused reports sub-resource.</para>
/// </summary>
[ApiController]
[Route("api/v1/payroll")]
[Authorize]
public sealed class PayrollReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PayrollReportsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// GET /api/v1/payroll/reports
    /// Lists the available payroll report types for the FE sidebar (US-PAY-009 §8). Includes a
    /// <c>deferred</c> flag for the Year-End Tax Statement stub.
    /// </summary>
    [HttpGet("reports")]
    [RequirePermission("Payroll.Export")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PayrollReportDescriptorDto>>), StatusCodes.Status200OK)]
    public IActionResult ListReportTypes([FromServices] HRM.Application.Common.Interfaces.IPayrollReportService service)
        => Ok(ApiResponse<IReadOnlyList<PayrollReportDescriptorDto>>.Ok(service.ListReportTypes()));

    /// <summary>
    /// GET /api/v1/payroll/reports/{reportType}
    /// Generates a payroll report (JSON preview) with filters (FR-1, FR-3). {reportType} is one of:
    /// PayrollSummary, EmployeeRegister, DepartmentSummary, StatutoryDeduction, BankAdvice, Ctc, Variance,
    /// YearEndTaxStatement (case-insensitive). BankAdvice account numbers are MASKED here (BR-2).
    /// </summary>
    [HttpGet("reports/{reportType}")]
    [RequirePermission("Payroll.Export")]
    [ProducesResponseType(typeof(ApiResponse<PayrollReportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReport(
        [FromRoute] string reportType,
        [FromQuery] PayrollReportQueryParams queryParams,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PayrollReportType>(reportType, ignoreCase: true, out var parsed))
            return StatusCode(400, ApiResponse.Fail($"Unknown report type '{reportType}'."));

        var result = await _mediator.Send(new GetPayrollReportQuery(parsed, queryParams), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PayrollReportResult>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/payroll/analytics/{chartType}
    /// Dashboard analytics data (FR-5), chart-library-agnostic. {chartType} is one of:
    /// MonthlyTrend, DepartmentCostDistribution, StatutoryBreakdown (case-insensitive).
    /// </summary>
    [HttpGet("analytics/{chartType}")]
    [RequirePermission("Payroll.Export")]
    [ProducesResponseType(typeof(ApiResponse<PayrollAnalyticsResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAnalytics(
        [FromRoute] string chartType,
        [FromQuery] PayrollReportQueryParams queryParams,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PayrollAnalyticsChartType>(chartType, ignoreCase: true, out var parsed))
            return StatusCode(400, ApiResponse.Fail($"Unknown chart type '{chartType}'."));

        var result = await _mediator.Send(new GetPayrollAnalyticsQuery(parsed, queryParams), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PayrollAnalyticsResult>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/payroll/reports/bank-advice/preview
    /// The bank-advice preview with account numbers MASKED (last 4 only, BR-2 / AC-2). Use the export
    /// endpoint (reportType=BankAdvice) to download the file with FULL account numbers.
    /// </summary>
    [HttpGet("reports/bank-advice/preview")]
    [RequirePermission("Payroll.Export")]
    [ProducesResponseType(typeof(ApiResponse<BankAdvicePreviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBankAdvicePreview(
        [FromQuery] PayrollReportQueryParams queryParams,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBankAdvicePreviewQuery(queryParams), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<BankAdvicePreviewDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/payroll/reports/bank-advice/full
    /// US-RPT-003 AC-4/FR-6/NFR-3: the bank-advice with FULL (unmasked) account numbers. Returns the SAME
    /// shape as the masked <c>bank-advice/preview</c> (un-masked account numbers). Gated by the dedicated
    /// <c>Payroll.ViewSensitive</c> permission (held by Tenant Owner / Tenant Admin / HR Manager — NOT HR
    /// Officer). Every call writes an audit record (action "PayrollReport.ViewSensitive"). Same query filters
    /// as the masked preview.
    /// </summary>
    [HttpGet("reports/bank-advice/full")]
    [RequirePermission("Payroll.ViewSensitive")]
    [ProducesResponseType(typeof(ApiResponse<BankAdvicePreviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevealBankAdvice(
        [FromQuery] PayrollReportQueryParams queryParams,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RevealBankAdviceQuery(queryParams), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<BankAdvicePreviewDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/payroll/reports/{reportType}/export?format=csv|xlsx|pdf
    /// Exports a report to a downloadable file (FR-2, AC-4). For BankAdvice the file carries FULL account
    /// numbers (BR-2). Synchronous — the async-large export path is a documented follow-up.
    /// </summary>
    [HttpGet("reports/{reportType}/export")]
    [RequirePermission("Payroll.Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportReport(
        [FromRoute] string reportType,
        [FromQuery] string format,
        [FromQuery] PayrollReportQueryParams queryParams,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PayrollReportType>(reportType, ignoreCase: true, out var parsedType))
            return StatusCode(400, ApiResponse.Fail($"Unknown report type '{reportType}'."));
        if (!Enum.TryParse<PayrollExportFormat>(format, ignoreCase: true, out var parsedFormat))
            return StatusCode(400, ApiResponse.Fail($"Unknown export format '{format}'. Use 'csv', 'xlsx' or 'pdf'."));

        var result = await _mediator.Send(
            new ExportPayrollReportQuery(parsedType, parsedFormat, queryParams), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var export = result.Value!;
        return File(export.FileContent, export.ContentType, export.FileName);
    }
}
