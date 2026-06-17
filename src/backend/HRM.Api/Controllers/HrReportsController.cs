using HRM.Application.Common.Interfaces;
using HRM.Application.DTOs;
using HRM.Application.Features.Reports.DTOs;
using HRM.Application.Features.Reports.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Pre-built HR reports for the HR Officer / HR Manager / Tenant Admin (US-RPT-001). All endpoints are
/// gated with the registered <c>Reports.View</c> permission. Reports read over the existing employee /
/// department / location / employment_history data and are tenant-scoped via the EF global query filter
/// (AC-5 / FR-7).
///
/// <para>Rooted at <c>api/v1/reports</c> (no <c>/tenant/</c> prefix) to match the sibling report endpoints
/// (/leaves, /payroll, /attendance). Reports are addressed by their KEBAB type key.</para>
/// </summary>
[ApiController]
[Route("api/v1/reports")]
[Authorize]
public sealed class HrReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public HrReportsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// GET /api/v1/reports
    /// Lists the available pre-built HR report types for the FE report-card catalog (AC-1): the kebab
    /// <c>type</c> key + a Material <c>icon</c> token. The FE owns i18n (title/description per type).
    /// </summary>
    [HttpGet]
    [RequirePermission("Reports.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<HrReportDescriptorDto>>), StatusCodes.Status200OK)]
    public IActionResult ListReportTypes([FromServices] IHrReportService service)
        => Ok(ApiResponse<IReadOnlyList<HrReportDescriptorDto>>.Ok(service.ListReportTypes()));

    /// <summary>
    /// POST /api/v1/reports/{type}/generate?refresh=false
    /// Generates a report with filters supplied in the request body (FR-1, FR-2). <c>{type}</c> is the kebab
    /// report key (case-insensitive): headcount, turnover, demographics, joiners-leavers,
    /// department-distribution, employment-type-breakdown. Set <c>?refresh=true</c> to bypass the cache (FR-8).
    /// </summary>
    [HttpPost("{type}/generate")]
    [RequirePermission("Reports.View")]
    [ProducesResponseType(typeof(ApiResponse<HrReportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateReport(
        [FromRoute] string type,
        [FromBody] HrReportQueryParams? queryParams,
        [FromQuery] bool refresh,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GenerateHrReportQuery(type, queryParams ?? new HrReportQueryParams(), refresh),
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<HrReportResult>.Ok(result.Value!));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  US-RPT-004 — Export reports to CSV / Excel / PDF
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/v1/reports/{type}/export
    /// Initiates an export of the report (US-RPT-004, FR-1/FR-5). ALWAYS returns JSON — never file bytes
    /// (the file is fetched from <c>/exports/{exportId}/download</c>). Small reports (&lt; 1000 rows) render
    /// synchronously and return <c>status: "Completed"</c>; large reports queue a Hangfire job and return
    /// <c>status: "Queued"</c>. Returns 429 when the caller already has 3 exports in progress (FR-10), 400 for an
    /// invalid report type or format.
    /// </summary>
    [HttpPost("{type}/export")]
    [RequirePermission("Reports.Export")]
    [ProducesResponseType(typeof(ApiResponse<HrReportExportInitiatedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ExportReport(
        [FromRoute] string type,
        [FromBody] HrReportExportRequest? request,
        [FromServices] IHrReportExportService exportService,
        CancellationToken cancellationToken)
    {
        request ??= new HrReportExportRequest();

        var result = await exportService.InitiateAsync(
            type,
            request.Filters ?? new HrReportQueryParams(),
            request.Format,
            request.IncludeCharts,
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<HrReportExportInitiatedDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/reports/exports
    /// The current user's recent exports (US-RPT-004 §8 history): id, reportType, format, status, requestedAt,
    /// completedAt, rowCount, fileSizeBytes, expiresAt, downloadReady.
    /// </summary>
    [HttpGet("exports")]
    [RequirePermission("Reports.Export")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<HrReportExportListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListExports(
        [FromServices] IHrReportExportService exportService,
        CancellationToken cancellationToken)
    {
        var result = await exportService.ListAsync(cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<HrReportExportListItemDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/reports/exports/{exportId}/download
    /// Downloads the rendered export FILE (US-RPT-004 AC-5). This is the ONLY endpoint that returns bytes
    /// (a <c>FileContentResult</c> with a Content-Disposition attachment). Tenant + owner scoped: 404 when the
    /// export is missing / cross-tenant / not the caller's, 410 when it has expired or been purged. Audits the
    /// download ("HrReport.ExportDownloaded").
    /// </summary>
    [HttpGet("exports/{exportId:guid}/download")]
    [RequirePermission("Reports.Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status410Gone)]
    public async Task<IActionResult> DownloadExport(
        [FromRoute] Guid exportId,
        [FromServices] IHrReportExportService exportService,
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
