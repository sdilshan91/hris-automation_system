using HRM.Application.DTOs;
using HRM.Application.Features.Performance.Commands;
using HRM.Application.Features.Performance.DTOs;
using HRM.Application.Features.Performance.Queries;
using HRM.Domain.Enums;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Tenant-scoped HR endpoints for appraisal-cycle management (US-PRF-004). Every write requires
/// <c>Performance.SetGoal.All</c> OR <c>Performance.Publish.All</c> (BR-1 — the [RequirePermission]
/// attribute admits either). All operations are tenant-scoped via the EF global query filter (NFR-2).
/// The active-cycle / dashboard reads serve the FE window gates (US-PRF-001/002/003 contract convergence).
/// </summary>
[ApiController]
[Route("api/v1/tenant/performance")]
[Authorize]
public sealed class CyclesController : ControllerBase
{
    private const string ManageCycles = "Performance.SetGoal.All";
    private const string PublishCycles = "Performance.Publish.All";

    // Team-/Self-level participation permissions (Manager / reviewer / employee) that must also be able
    // to READ the active cycle + its window flags — the module landing-route data source (BUG-244 #8).
    private const string SetGoalTeam = "Performance.SetGoal.Team";
    private const string ReviewTeam = "Performance.Review.Team";
    private const string ReadSelf = "Performance.Read.Self";

    private readonly IMediator _mediator;

    public CyclesController(IMediator mediator) => _mediator = mediator;

    /// <summary>GET /api/v1/tenant/performance/cycles — lists cycles, optionally filtered by status.</summary>
    [HttpGet("cycles")]
    [RequirePermission(ManageCycles, PublishCycles)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CycleSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] AppraisalCycleStatus? status, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListCyclesQuery(status), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<IReadOnlyList<CycleSummaryDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/tenant/performance/cycles/active — the canonical active cycle with its current phase and
    /// authoritative window flags (goalSettingOpen / selfAssessmentOpen / managerReviewOpen). AC-3.
    /// </summary>
    [HttpGet("cycles/active")]
    [RequirePermission(ManageCycles, PublishCycles, SetGoalTeam, ReviewTeam, ReadSelf)]
    [ProducesResponseType(typeof(ApiResponse<ActiveCycleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetActive(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetActiveCycleQuery(), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<ActiveCycleDto>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/tenant/performance/cycles/{id} — a single cycle with its phases.</summary>
    [HttpGet("cycles/{id:guid}", Name = "GetCycleById")]
    [RequirePermission(ManageCycles, PublishCycles)]
    [ProducesResponseType(typeof(ApiResponse<CycleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCycleByIdQuery(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<CycleDto>.Ok(result.Value!));
    }

    /// <summary>GET /api/v1/tenant/performance/cycles/{id}/dashboard — timeline + per-phase completion stats (AC-3).</summary>
    [HttpGet("cycles/{id:guid}/dashboard")]
    [RequirePermission(ManageCycles, PublishCycles)]
    [ProducesResponseType(typeof(ApiResponse<CycleDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDashboard(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCycleDashboardQuery(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<CycleDashboardDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/tenant/performance/cycles — creates a cycle, resolves participants, schedules jobs (AC-1/AC-2).</summary>
    [HttpPost("cycles")]
    [RequirePermission(ManageCycles, PublishCycles)]
    [ProducesResponseType(typeof(ApiResponse<CycleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateCycleInput input, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateCycleCommand(input), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return CreatedAtRoute("GetCycleById", new { id = result.Value!.Id }, ApiResponse<CycleDto>.Ok(result.Value!));
    }

    /// <summary>PUT /api/v1/tenant/performance/cycles/{id} — edits a cycle / extends phases (AC-5).</summary>
    [HttpPut("cycles/{id:guid}")]
    [RequirePermission(ManageCycles, PublishCycles)]
    [ProducesResponseType(typeof(ApiResponse<CycleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCycleInput input, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateCycleCommand(id, input), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<CycleDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/tenant/performance/cycles/clone — clones a cycle as a template with new dates (FR-8).</summary>
    [HttpPost("cycles/clone")]
    [RequirePermission(ManageCycles, PublishCycles)]
    [ProducesResponseType(typeof(ApiResponse<CycleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Clone([FromBody] CloneCycleInput input, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CloneCycleCommand(input), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return CreatedAtRoute("GetCycleById", new { id = result.Value!.Id }, ApiResponse<CycleDto>.Ok(result.Value!));
    }

    /// <summary>POST /api/v1/tenant/performance/cycles/{id}/status — status transition (FR-7); Cancel needs a reason (BR-6).</summary>
    [HttpPost("cycles/{id:guid}/status")]
    [RequirePermission(ManageCycles, PublishCycles)]
    [ProducesResponseType(typeof(ApiResponse<CycleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Transition(
        Guid id, [FromBody] TransitionCycleStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new TransitionCycleStatusCommand(id, request.TargetStatus, request.Reason), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<CycleDto>.Ok(result.Value!));
    }

    /// <summary>DELETE /api/v1/tenant/performance/cycles/{id} — deletes a Draft cycle with no reviews (BR-2; else cancel).</summary>
    [HttpDelete("cycles/{id:guid}")]
    [RequirePermission(ManageCycles, PublishCycles)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteCycleCommand(id), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse.Ok());
    }

    // ── Calibration (US-PRF-011) ─────────────────────────────────────────

    // The calibration cohort is READ through the dashboard's scope resolver (Performance.View.All/Team), so it
    // is gated with the same view permissions as the dashboard. Applying a calibration is an HR-level WRITE,
    // gated (like publishing) on Publish.All / Manage / Review.All.
    private const string ViewAll = "Performance.View.All";
    private const string ViewTeam = "Performance.View.Team";
    private const string ManagePerf = "Performance.Manage";
    private const string ReviewAll = "Performance.Review.All";

    /// <summary>
    /// GET /api/v1/tenant/performance/cycles/{id}/calibration?departmentId= — the calibration cohort for the
    /// cycle (US-PRF-011 §2): each in-scope employee's original + calibrated rating, reviewer and department.
    /// Reuses the dashboard scope + population logic; a manager sees only their direct reports.
    /// </summary>
    [HttpGet("cycles/{id:guid}/calibration")]
    [RequirePermission(ViewAll, ViewTeam)]
    [ProducesResponseType(typeof(ApiResponse<CalibrationCohortDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCalibrationCohort(
        Guid id, [FromQuery] Guid? departmentId, CancellationToken cancellationToken)
    {
        var filter = new PerformanceDashboardFilter { CycleId = id, DepartmentId = departmentId };
        var result = await _mediator.Send(new GetCalibrationCohortQuery(filter), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<CalibrationCohortDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/tenant/performance/cycles/{id}/calibration — applies/adjusts a calibrated rating for one
    /// employee with a MANDATORY reason (US-PRF-011 §3). Never overwrites the review's original score; records
    /// an append-only history row + an audit entry.
    /// </summary>
    [HttpPost("cycles/{id:guid}/calibration")]
    [RequirePermission(PublishCycles, ManagePerf, ReviewAll)]
    [ProducesResponseType(typeof(ApiResponse<CalibrationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ApplyCalibration(
        Guid id, [FromBody] ApplyCalibrationRequest request, CancellationToken cancellationToken)
    {
        var input = new ApplyCalibrationInput(id, request.EmployeeId, request.CalibratedScore, request.Reason);
        var result = await _mediator.Send(new ApplyCalibrationCommand(input), cancellationToken);
        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!, result.ErrorCode));
        return Ok(ApiResponse<CalibrationResultDto>.Ok(result.Value!));
    }
}

/// <summary>Body for the status-transition endpoint (US-PRF-004 FR-7/BR-6).</summary>
public sealed record TransitionCycleStatusRequest(AppraisalCycleStatus TargetStatus, string? Reason);

/// <summary>Body for the apply-calibration endpoint (US-PRF-011 §3). Cycle id comes from the route.</summary>
public sealed record ApplyCalibrationRequest(Guid EmployeeId, decimal CalibratedScore, string Reason);
