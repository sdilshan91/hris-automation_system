using HRM.Application.Common.Interfaces;
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
    // AC-3: the statement endpoints return file BYTES, so they call the service directly rather than routing a
    // byte[] payload through MediatR.
    private readonly IPayrollReportService _reports;

    public PayrollReportsController(IMediator mediator, IPayrollReportService reports)
    {
        _mediator = mediator;
        _reports = reports;
    }

    // ── US-PAY-009 AC-3: individual + bulk year-end tax statements ─────────────
    //
    // The columnar YearEndTaxStatement report already shipped, but AC-3 asks for INDIVIDUAL per-employee
    // statements with a MONTH-WISE breakdown, available for bulk download — three things a table of annual
    // totals cannot satisfy.

    /// <summary>
    /// GET /api/v1/payroll/tax-statements/{employeeId}?year=2026
    /// One employee's year-end tax statement as a PDF.
    /// </summary>
    [HttpGet("tax-statements/{employeeId:guid}")]
    [RequirePermission("Payroll.Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaxStatement(
        Guid employeeId, [FromQuery] int year, CancellationToken cancellationToken)
    {
        var result = await _reports.GetYearEndTaxStatementPdfAsync(employeeId, year, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return File(result.Value!.FileContent, result.Value.ContentType, result.Value.FileName);
    }

    /// <summary>
    /// GET /api/v1/payroll/tax-statements/bundle?year=2026
    /// Every employee's statement for the year, as a ZIP (AC-3 bulk download).
    /// </summary>
    [HttpGet("tax-statements/bundle")]
    [RequirePermission("Payroll.Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaxStatementBundle(
        [FromQuery] int year, CancellationToken cancellationToken)
    {
        var result = await _reports.GetYearEndTaxStatementsBundleAsync(year, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return File(result.Value!.FileContent, result.Value.ContentType, result.Value.FileName);
    }

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

    // ════════════════════════════════════════════════════════════════════════
    //  ISSUE-178 PR2 — async large-report export (sync fast-path + Hangfire)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/v1/payroll/reports/{reportType}/export
    /// Initiates an export of the payroll report (ISSUE-178 PR2). ALWAYS returns JSON — never file bytes (the
    /// file is fetched from <c>/exports/{exportId}/download</c>). Small reports (&lt; 1000 rows) render
    /// synchronously and return <c>status: "Completed"</c>; large reports queue a Hangfire job and return
    /// <c>status: "Queued"</c>. Returns 429 when the caller already has 3 exports in progress, 400 for an invalid
    /// report type or format. The synchronous <c>GET .../export</c> endpoint above is retained for back-compat.
    /// </summary>
    [HttpPost("reports/{reportType}/export")]
    [RequirePermission("Payroll.Export")]
    [ProducesResponseType(typeof(ApiResponse<PayrollReportExportInitiatedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> InitiateExport(
        [FromRoute] string reportType,
        [FromBody] PayrollReportExportRequest? request,
        [FromServices] IPayrollReportExportService exportService,
        CancellationToken cancellationToken)
    {
        request ??= new PayrollReportExportRequest();

        var result = await exportService.InitiateAsync(
            reportType,
            request.Filters ?? new PayrollReportQueryParams(),
            request.Format,
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PayrollReportExportInitiatedDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/payroll/reports/exports
    /// The current user's recent payroll-report exports (ISSUE-178 PR2 history): id, reportType, format, status,
    /// requestedAt, completedAt, rowCount, fileSizeBytes, expiresAt, downloadReady.
    /// </summary>
    [HttpGet("reports/exports")]
    [RequirePermission("Payroll.Export")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PayrollReportExportListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListExports(
        [FromServices] IPayrollReportExportService exportService,
        CancellationToken cancellationToken)
    {
        var result = await exportService.ListAsync(cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<PayrollReportExportListItemDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/payroll/reports/exports/{exportId}/download
    /// Downloads the rendered export FILE (ISSUE-178 PR2). This is the ONLY new endpoint that returns bytes (a
    /// <c>FileContentResult</c> with a Content-Disposition attachment). Tenant + owner scoped: 404 when the export
    /// is missing / cross-tenant / not the caller's, 410 when it has expired or been purged. Audits the download
    /// ("PayrollReport.ExportDownloaded").
    /// </summary>
    [HttpGet("reports/exports/{exportId:guid}/download")]
    [RequirePermission("Payroll.Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status410Gone)]
    public async Task<IActionResult> DownloadExport(
        [FromRoute] Guid exportId,
        [FromServices] IPayrollReportExportService exportService,
        CancellationToken cancellationToken)
    {
        var result = await exportService.GetForDownloadAsync(exportId, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var download = result.Value;
        if (download is null)
            return NotFound(ApiResponse.Fail("Export not found.", "export_not_found"));

        if (download.Expired)
            return StatusCode(StatusCodes.Status410Gone,
                ApiResponse.Fail("The export has expired and the file has been deleted.", "export_expired"));

        return File(download.Content, download.ContentType, download.FileName);
    }
}
