using HRM.Application.DTOs;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Application.Features.Payroll.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Payroll history + audit trail for HR Officer / Tenant Admin / Auditor (US-PAY-012). Exposes the payroll
/// run history (FR-1/AC-1), the payroll-specific audit trail with filtering (FR-4/AC-4), the per-run audit
/// timeline (FR-6/AC-2) and audit export (FR-5).
///
/// <para>PERMISSIONS: the history + audit-trail + timeline reads are gated on <c>Payroll.View</c> (held by
/// Tenant Admin, HR Manager, HR Officer, and Auditor — the audit consumers). Export reuses
/// <c>Payroll.Export</c> (the report-export gate). All reads are tenant-scoped (AC-5).</para>
///
/// <para>IMMUTABILITY (NFR-4/BR-2): this controller exposes only GET reads + an export — there is NO
/// update/delete endpoint for audit records. Audit data is append-only and tamper-proof.</para>
/// </summary>
[ApiController]
[Route("api/v1/payroll")]
[Authorize]
public sealed class PayrollAuditController : ControllerBase
{
    private readonly IMediator _mediator;

    public PayrollAuditController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// GET /api/v1/payroll/runs/history
    /// FR-1/AC-1: the payroll run history — Pay Period, Status, Employee Count, Total Net, Initiated By,
    /// Approved By, Finalized Date — filterable by year/month/status, sortable, paginated.
    /// </summary>
    [HttpGet("runs/history")]
    [RequirePermission("Payroll.View")]
    [ProducesResponseType(typeof(ApiResponse<PayrollRunHistoryPageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRunHistory(
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] string? status,
        [FromQuery] string? sortBy,
        [FromQuery] bool descending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var filter = new PayrollRunHistoryFilter
        {
            Year = year, Month = month, Status = status,
            SortBy = sortBy, Descending = descending, Page = page, PageSize = pageSize,
        };
        var result = await _mediator.Send(new GetPayrollRunHistoryQuery(filter), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PayrollRunHistoryPageDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/payroll/audit-trail
    /// FR-4/AC-4: the payroll audit trail filtered by date range / action / actor / resource type / resource
    /// id, paginated. Tenant-scoped (AC-5).
    /// </summary>
    [HttpGet("audit-trail")]
    [RequirePermission("Payroll.View")]
    [ProducesResponseType(typeof(ApiResponse<PayrollAuditPageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditTrail(
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? action,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? resourceType,
        [FromQuery] string? resourceId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var filter = BuildTrailFilter(fromUtc, toUtc, action, actorUserId, resourceType, resourceId, page, pageSize);
        var result = await _mediator.Send(new GetPayrollAuditTrailQuery(filter), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<PayrollAuditPageDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/payroll/runs/{runId}/audit-timeline
    /// FR-6/AC-2: the per-run audit timeline — every audit entry for the run (and its payslips) in
    /// chronological order. 404 when the run is not visible to the tenant (AC-5).
    /// </summary>
    [HttpGet("runs/{runId:guid}/audit-timeline")]
    [RequirePermission("Payroll.View")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PayrollAuditEntryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRunTimeline(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPayrollRunAuditTimelineQuery(runId), cancellationToken);
        return result.IsFailure
            ? StatusCode(result.StatusCode ?? 404, ApiResponse.Fail(result.Error!, result.ErrorCode))
            : Ok(ApiResponse<IReadOnlyList<PayrollAuditEntryDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/payroll/audit-trail/export?format=csv|xlsx|pdf
    /// FR-5: exports the filtered audit trail to a downloadable file. Reuses the US-PAY-009 report renderer.
    /// </summary>
    [HttpGet("audit-trail/export")]
    // ISSUE-185: gate on Payroll.View (the same permission as the trail READ above), NOT Payroll.Export. The
    // export is just a downloadable form of the trail the caller can already see; gating it on Payroll.Export
    // locked out the Auditor role — the designated audit consumer (AC-4) — which holds Payroll.View but not
    // Payroll.Export. Anyone who can view the trail can export it; no wider payroll-data export is unlocked.
    [RequirePermission("Payroll.View")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportAuditTrail(
        [FromQuery] string format,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? action,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? resourceType,
        [FromQuery] string? resourceId,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<PayrollExportFormat>(format, ignoreCase: true, out var parsedFormat))
            return StatusCode(400, ApiResponse.Fail($"Unknown export format '{format}'. Use 'csv', 'xlsx' or 'pdf'."));

        var filter = BuildTrailFilter(fromUtc, toUtc, action, actorUserId, resourceType, resourceId, page: 1, pageSize: 50);
        var result = await _mediator.Send(new ExportPayrollAuditTrailQuery(filter, parsedFormat), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));

        var export = result.Value!;
        return File(export.FileContent, export.ContentType, export.FileName);
    }

    private static PayrollAuditTrailFilter BuildTrailFilter(
        DateTime? fromUtc, DateTime? toUtc, string? action, Guid? actorUserId,
        string? resourceType, string? resourceId, int page, int pageSize) => new()
    {
        FromUtc = fromUtc, ToUtc = toUtc, Action = action, ActorUserId = actorUserId,
        ResourceType = resourceType, ResourceId = resourceId, Page = page, PageSize = pageSize,
    };
}
