using HRM.Application.DTOs;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Recruitment.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Recruiter/HR-facing recruitment dashboard + analytics endpoints (US-REC-009). Read-only aggregation
/// over the existing recruitment data, tenant-scoped via the EF global query filter (AC-5). Both endpoints
/// require <c>Recruitment.View</c> — the story references <c>Reports.View.*</c> which is not a concrete
/// permission in <c>PermissionCatalog</c>; <c>Recruitment.View</c> is the existing recruiter read
/// permission and is reused (same posture as the rest of the module).
///
/// DEFERRALS (documented in the service): PDF export (FR-8 — CSV + XLSX only here), async Hangfire export
/// for large datasets (NFR-5), Redis pre-aggregation + the materialized view (NFR-3), and Postgres RLS
/// (NFR-2, separate US-PLT-002). The dashboard is computed live on each request.
/// </summary>
[ApiController]
[Route("api/v1/recruitment")]
[Authorize]
public sealed class RecruitmentDashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public RecruitmentDashboardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// GET /api/v1/recruitment/dashboard?from=yyyy-MM-dd&amp;to=yyyy-MM-dd&amp;departmentId=&amp;vacancyId=
    /// The recruitment dashboard: KPI cards, pipeline funnel + conversions, source effectiveness,
    /// time-to-hire trend, vacancy-status summary and the recent-activity feed (AC-1..AC-4/FR-1..FR-5/FR-9).
    /// The date range narrows the period metrics (FR-6); department/vacancy drill in (FR-7). Defaults to the
    /// last 30 days (UTC). Requires Recruitment.View.
    /// </summary>
    [HttpGet("dashboard")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(typeof(ApiResponse<RecruitmentDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? vacancyId,
        CancellationToken cancellationToken)
    {
        var filter = new RecruitmentDashboardFilter
        {
            From = from,
            To = to,
            DepartmentId = departmentId,
            VacancyId = vacancyId,
        };

        var result = await _mediator.Send(new RecruitmentDashboardQuery(filter), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<RecruitmentDashboardDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/recruitment/dashboard/export?from=&amp;to=&amp;format=csv|xlsx&amp;departmentId=&amp;vacancyId=
    /// Exports the dashboard's tabular data (KPIs + funnel + source effectiveness) as a CSV/XLSX file
    /// download (FR-8). PDF + async large-dataset export are deferred. Requires Recruitment.View.
    /// </summary>
    [HttpGet("dashboard/export")]
    [RequirePermission("Recruitment.View")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportDashboard(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? vacancyId,
        [FromQuery] string? format,
        CancellationToken cancellationToken)
    {
        var filter = new RecruitmentDashboardFilter
        {
            From = from,
            To = to,
            DepartmentId = departmentId,
            VacancyId = vacancyId,
        };

        var result = await _mediator.Send(
            new ExportRecruitmentDashboardQuery(filter, format ?? "csv"), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var export = result.Value!;
        return File(export.FileContent, export.ContentType, export.FileName);
    }
}
