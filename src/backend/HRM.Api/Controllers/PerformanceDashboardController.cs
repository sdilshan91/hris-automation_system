using HRM.Application.DTOs;
using HRM.Application.Features.Performance.DTOs;
using HRM.Application.Features.Performance.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped performance dashboard + analytics endpoints (US-PRF-007). Read-only aggregation over the
/// existing performance + Core HR data, tenant-isolated via the EF global query filter (NFR-2/AC-5).
///
/// Permission posture: the story names <c>Performance.Read.All</c> / <c>Performance.Read.Team</c>, which are
/// NOT concrete permissions in <c>PermissionCatalog</c>. The existing equivalents are reused —
/// <c>Performance.View.All</c> (HR / org-wide, granted to HR Officer / HR Manager / Tenant Admin) and
/// <c>Performance.View.Team</c> (Manager). Every action admits EITHER; the service then resolves the scope:
/// View.All ⇒ org-wide data + top/bottom performers, View.Team ⇒ ONLY the caller's direct reports + a team
/// ranking (BR-1/BR-3/AC-5). Employees hold neither, so they are forbidden server-side (FE also redirects).
///
/// DEFERRALS (documented in the service): PDF export (FR-8 — CSV + XLSX only here, QuestPDF seam) and the
/// NFR-3/BR-4 materialized-view + Redis refresh (computed live each request — clearly-marked extension point).
/// </summary>
[ApiController]
[Route("api/v1/tenant/performance")]
[Authorize]
public sealed class PerformanceDashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public PerformanceDashboardController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// GET /api/v1/tenant/performance/dashboard/overview
    ///   ?cycleId=&amp;departmentId=&amp;gradeId=&amp;employmentType=&amp;locationId=&amp;topBottomCount=10&amp;includeProbation=false
    /// The dashboard overview (AC-1/AC-2/FR-1..FR-3/FR-6): completion rate, average score, score-distribution
    /// histogram, department-wise averages, top/bottom performers (team ranking for a manager) and cycle
    /// progress metrics. All filters combine (FR-4). Defaults to the tenant's most recent cycle.
    /// </summary>
    [HttpGet("dashboard/overview")]
    [RequirePermission("Performance.View.All", "Performance.View.Team")]
    [ProducesResponseType(typeof(ApiResponse<PerformanceDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOverview(
        [FromQuery] Guid? cycleId,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? gradeId,
        [FromQuery] string? employmentType,
        [FromQuery] Guid? locationId,
        [FromQuery] int? topBottomCount,
        [FromQuery] bool? includeProbation,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilter(cycleId, departmentId, gradeId, employmentType, locationId, topBottomCount, includeProbation);
        var result = await _mediator.Send(new PerformanceDashboardOverviewQuery(filter), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PerformanceDashboardDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/dashboard/department/{departmentId}?cycleId=&amp;…
    /// Department drill-down (FR-5/AC-2): the department's employees with their individual final scores and
    /// review status for the cycle. Manager scope still applies (a manager only sees their own reports here).
    /// </summary>
    [HttpGet("dashboard/department/{departmentId:guid}")]
    [RequirePermission("Performance.View.All", "Performance.View.Team")]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDrilldownDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDepartmentDrilldown(
        Guid departmentId,
        [FromQuery] Guid? cycleId,
        [FromQuery] Guid? gradeId,
        [FromQuery] string? employmentType,
        [FromQuery] Guid? locationId,
        [FromQuery] bool? includeProbation,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilter(cycleId, departmentId, gradeId, employmentType, locationId, null, includeProbation);
        var result = await _mediator.Send(
            new PerformanceDepartmentDrilldownQuery(departmentId, filter), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<DepartmentDrilldownDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/dashboard/trend?cycleIds=g1&amp;cycleIds=g2&amp;includeDepartmentSeries=false&amp;…
    /// Multi-cycle trend (AC-3/FR-7): the average-score series across the selected cycles (all cycles when
    /// none supplied), optionally with a per-department overlay. The same FR-4 filters and caller scope apply.
    /// </summary>
    [HttpGet("dashboard/trend")]
    [RequirePermission("Performance.View.All", "Performance.View.Team")]
    [ProducesResponseType(typeof(ApiResponse<PerformanceTrendDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTrend(
        [FromQuery] Guid[]? cycleIds,
        [FromQuery] bool? includeDepartmentSeries,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? gradeId,
        [FromQuery] string? employmentType,
        [FromQuery] Guid? locationId,
        [FromQuery] bool? includeProbation,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilter(null, departmentId, gradeId, employmentType, locationId, null, includeProbation);
        var result = await _mediator.Send(
            new PerformanceTrendQuery(cycleIds ?? [], filter, includeDepartmentSeries ?? false), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PerformanceTrendDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/dashboard/export?format=csv|xlsx&amp;cycleId=&amp;…
    /// Exports the overview's tabular data (overview + progress + distribution + department averages +
    /// top/bottom performers) as a CSV/XLSX file download (FR-8/AC-4). PDF is deferred. Scope applies.
    /// </summary>
    [HttpGet("dashboard/export")]
    [RequirePermission("Performance.View.All", "Performance.View.Team")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportDashboard(
        [FromQuery] string? format,
        [FromQuery] Guid? cycleId,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? gradeId,
        [FromQuery] string? employmentType,
        [FromQuery] Guid? locationId,
        [FromQuery] int? topBottomCount,
        [FromQuery] bool? includeProbation,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilter(cycleId, departmentId, gradeId, employmentType, locationId, topBottomCount, includeProbation);
        var result = await _mediator.Send(
            new ExportPerformanceDashboardQuery(filter, format ?? "csv"), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var export = result.Value!;
        return File(export.FileContent, export.ContentType, export.FileName);
    }

    private static PerformanceDashboardFilter BuildFilter(
        Guid? cycleId, Guid? departmentId, Guid? gradeId, string? employmentType,
        Guid? locationId, int? topBottomCount, bool? includeProbation)
        => new()
        {
            CycleId = cycleId,
            DepartmentId = departmentId,
            GradeId = gradeId,
            EmploymentType = employmentType,
            LocationId = locationId,
            TopBottomCount = topBottomCount ?? 10,
            IncludeProbation = includeProbation ?? false,
        };
}
