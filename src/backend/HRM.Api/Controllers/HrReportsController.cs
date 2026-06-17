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
}
