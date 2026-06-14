using HRM.Application.DTOs;
using HRM.Application.Features.Attendance.Commands;
using HRM.Application.Features.Attendance.Queries;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Employee attendance endpoints (US-ATT-001).
/// Employees clock in from the browser; the punch is tenant-scoped via the resolved tenant
/// context and the acting employee is resolved from the JWT.
/// </summary>
[ApiController]
[Route("api/v1/attendance")]
[Authorize]
public sealed class AttendanceController : ControllerBase
{
    private readonly IMediator _mediator;

    public AttendanceController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// POST /api/v1/attendance/clock-in
    /// Clocks the current employee in (FR-1..FR-5, AC-1..AC-5). The source IP, user agent, and the
    /// optional Idempotency-Key header are captured here from the HTTP context and passed to the
    /// command (NFR-4). Coordinates / photo are conditional on the tenant attendance policy.
    /// Gated by Attendance.CheckIn — the self clock-in permission held by the Employee role.
    /// </summary>
    [HttpPost("clock-in")]
    [RequirePermission("Attendance.CheckIn")]
    [ProducesResponseType(typeof(ApiResponse<AttendanceLogDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ClockIn(
        [FromBody] ClockInRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.FirstOrDefault();

        var command = new ClockInCommand(
            request.Latitude,
            request.Longitude,
            request.PhotoUrl,
            request.Source,
            ipAddress,
            userAgent,
            idempotencyKey);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<AttendanceLogDto>.Ok(result.Value!, "Clocked in."));
    }

    /// <summary>
    /// POST /api/v1/attendance/clock-out
    /// Clocks the current employee out and auto-calculates work hours (US-ATT-002 FR-1..FR-4/FR-7,
    /// AC-1..AC-5, BR-1..BR-4/BR-6). The source IP and user agent are captured here from the HTTP
    /// context and passed to the command (§7); clock_out is set server-side in UTC. Coordinates are
    /// conditional on the tenant geolocation policy (AC-5). Returns 404 when there is no open record.
    /// Gated by Attendance.CheckIn — the same self permission as clock-in.
    /// </summary>
    [HttpPost("clock-out")]
    [RequirePermission("Attendance.CheckIn")]
    [ProducesResponseType(typeof(ApiResponse<ClockOutResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClockOut(
        [FromBody] ClockOutRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.FirstOrDefault();

        var command = new ClockOutCommand(
            request.Latitude,
            request.Longitude,
            ipAddress,
            userAgent);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ClockOutResultDto>.Ok(result.Value!, "Clocked out."));
    }

    /// <summary>
    /// GET /api/v1/attendance/status
    /// Returns the current clock-in status for the authenticated employee in the current tenant:
    /// whether an open AttendanceLog exists (and when it started), the tenant's RequireGeolocation
    /// flag (BR-2, drives the UI's AC-3 vs AC-4 branch), and null shift fields (US-ATT-005 not built).
    /// Gated by Attendance.CheckIn — the same self permission as clock-in.
    /// </summary>
    [HttpGet("status")]
    [RequirePermission("Attendance.CheckIn")]
    [ProducesResponseType(typeof(ApiResponse<ClockStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetClockStatusQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ClockStatusDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/attendance/regularizations
    /// Submits an attendance regularization request for the authenticated employee (US-ATT-003
    /// FR-1/FR-2/FR-5..FR-7, AC-1..AC-5, BR-2..BR-7). Creates a PENDING record; the underlying
    /// attendance_log is not mutated until manager approval (US-ATT-004). Returns 400 (future date,
    /// inconsistent times, lookback exceeded), 403 (no employee linked / inactive), or 409
    /// (duplicate pending / locked payroll period). Gated by Attendance.Regularize.Self.
    /// </summary>
    [HttpPost("regularizations")]
    [RequirePermission("Attendance.Regularize.Self")]
    [ProducesResponseType(typeof(ApiResponse<RegularizationDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitRegularization(
        [FromBody] SubmitRegularizationRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SubmitRegularizationCommand(request), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<RegularizationDto>.Ok(result.Value!, "Regularization request submitted."));
    }

    /// <summary>
    /// GET /api/v1/attendance/regularizations
    /// Returns the authenticated employee's own regularization requests, most recent first (US-ATT-003
    /// §8 — drives the attendance-history status pills). Gated by Attendance.Regularize.Self.
    /// </summary>
    [HttpGet("regularizations")]
    [RequirePermission("Attendance.Regularize.Self")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RegularizationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyRegularizations(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyRegularizationsQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<RegularizationDto>>.Ok(result.Value!));
    }

    // ══════════════════════════════════════════════════════════════
    //  US-ATT-004: Manager approve/reject of regularization requests
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/v1/attendance/regularizations/pending
    /// Returns the current manager's pending regularization-approval queue: pending requests from
    /// their direct reports with employee name, date, requested times, reason, and submitted-on
    /// (US-ATT-004 FR-1/AC-3). Supports optional employeeId / fromDate / toDate filters.
    /// Gated by Attendance.Approve.Team.
    /// </summary>
    [HttpGet("regularizations/pending")]
    [RequirePermission("Attendance.Approve.Team")]
    [ProducesResponseType(typeof(ApiResponse<PendingRegularizationQueueResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPendingRegularizations(
        [FromQuery] Guid? employeeId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken cancellationToken)
    {
        var queryParams = new PendingRegularizationQueryParams
        {
            EmployeeId = employeeId,
            FromDate = fromDate,
            ToDate = toDate,
        };

        var result = await _mediator.Send(
            new GetPendingRegularizationsQuery(queryParams), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<PendingRegularizationQueueResult>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/attendance/regularizations/{id}/approve
    /// Approves a pending regularization from one of the current manager's direct reports as the final
    /// (single-level) approver (US-ATT-004 AC-1/FR-2). Sets status APPROVED, creates/updates the
    /// attendance_log with the regularized times and recalculated work hours, and records an immutable
    /// decision-history entry — all atomically (NFR-2). Comment is optional (BR-2). Returns 403 when
    /// the manager is not the request owner's manager (AC-5) or it is the manager's own request (BR-6),
    /// 404 when not found, 409 when already actioned (BR-3) or the date is now in a locked payroll
    /// period (BR-5). Gated by Attendance.Approve.Team.
    /// </summary>
    [HttpPost("regularizations/{id:guid}/approve")]
    [RequirePermission("Attendance.Approve.Team")]
    [ProducesResponseType(typeof(ApiResponse<RegularizationDecisionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveRegularization(
        [FromRoute] Guid id,
        [FromBody] ApproveRegularizationRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ApproveRegularizationCommand(id, request?.Comment), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<RegularizationDecisionDto>.Ok(result.Value!, "Regularization approved."));
    }

    /// <summary>
    /// POST /api/v1/attendance/regularizations/{id}/reject
    /// Rejects a pending regularization from one of the current manager's direct reports
    /// (US-ATT-004 AC-2/FR-3). Sets status REJECTED and records the mandatory reason (min 10 chars,
    /// BR-1) in an immutable decision-history entry; the attendance_log is not touched. Same
    /// authorization/immutability/self-approval rules as approve. Gated by Attendance.Approve.Team.
    /// </summary>
    [HttpPost("regularizations/{id:guid}/reject")]
    [RequirePermission("Attendance.Approve.Team")]
    [ProducesResponseType(typeof(ApiResponse<RegularizationDecisionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectRegularization(
        [FromRoute] Guid id,
        [FromBody] RejectRegularizationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RejectRegularizationCommand(id, request.Reason), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<RegularizationDecisionDto>.Ok(result.Value!, "Regularization rejected."));
    }

    /// <summary>
    /// POST /api/v1/attendance/regularizations/bulk-approve
    /// Bulk-approves multiple pending regularizations in one action (US-ATT-004 BR-7). Each id runs the
    /// full per-item authorization + immutability + payroll-lock pipeline independently; per-item
    /// results are returned so the FE can show a partial-success summary. Returns 400 when the id list
    /// is empty. Gated by Attendance.Approve.Team.
    /// </summary>
    [HttpPost("regularizations/bulk-approve")]
    [RequirePermission("Attendance.Approve.Team")]
    [ProducesResponseType(typeof(ApiResponse<BulkApproveRegularizationResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> BulkApproveRegularizations(
        [FromBody] BulkApproveRegularizationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new BulkApproveRegularizationsCommand(request.RegularizationIds, request.Comment),
            cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<BulkApproveRegularizationResult>.Ok(result.Value!, "Bulk approval processed."));
    }

    // ══════════════════════════════════════════════════════════════
    //  US-ATT-005: Shift management and assignment per employee
    //  All gated by Attendance.Shift.Manage (HR-level).
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/v1/attendance/shifts
    /// Lists every shift defined for the current tenant, including the active assigned-employee count
    /// per shift (US-ATT-005 AC-1). Gated by Attendance.Shift.Manage.
    /// </summary>
    [HttpGet("shifts")]
    [RequirePermission("Attendance.Shift.Manage")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ShiftDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetShifts(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetShiftsQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<ShiftDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/attendance/shifts
    /// Creates a new shift definition (US-ATT-005 AC-1/FR-1/FR-2). Rejects a duplicate name (409
    /// "duplicate_name"). Gated by Attendance.Shift.Manage.
    /// </summary>
    [HttpPost("shifts")]
    [RequirePermission("Attendance.Shift.Manage")]
    [ProducesResponseType(typeof(ApiResponse<ShiftDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateShift(
        [FromBody] ShiftRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateShiftCommand(request), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<ShiftDto>.Ok(result.Value!, "Shift created."));
    }

    /// <summary>
    /// PUT /api/v1/attendance/shifts/{id}
    /// Updates a shift definition (US-ATT-005 FR-2). Gated by Attendance.Shift.Manage.
    /// </summary>
    [HttpPut("shifts/{id:guid}")]
    [RequirePermission("Attendance.Shift.Manage")]
    [ProducesResponseType(typeof(ApiResponse<ShiftDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateShift(
        [FromRoute] Guid id, [FromBody] ShiftRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateShiftCommand(id, request), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ShiftDto>.Ok(result.Value!, "Shift updated."));
    }

    /// <summary>
    /// DELETE /api/v1/attendance/shifts/{id}
    /// Deletes a shift (US-ATT-005 AC-4/FR-6). Returns 204 on success; 409 "shift_in_use" when the
    /// shift has active assignments. Gated by Attendance.Shift.Manage.
    /// </summary>
    [HttpDelete("shifts/{id:guid}")]
    [RequirePermission("Attendance.Shift.Manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteShift(
        [FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteShiftCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return NoContent();
    }

    /// <summary>
    /// POST /api/v1/attendance/shifts/{id}/clone
    /// Clones a shift to create a variant named "Copy of {original}" (US-ATT-005 FR-8).
    /// Gated by Attendance.Shift.Manage.
    /// </summary>
    [HttpPost("shifts/{id:guid}/clone")]
    [RequirePermission("Attendance.Shift.Manage")]
    [ProducesResponseType(typeof(ApiResponse<ShiftDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CloneShift(
        [FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CloneShiftCommand(id), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ShiftDto>.Ok(result.Value!, "Shift cloned."));
    }

    /// <summary>
    /// POST /api/v1/attendance/shifts/{id}/assign
    /// Bulk-assigns a shift to employees with an effective date (US-ATT-005 AC-2/AC-3/FR-3). Closes any
    /// overlapping current assignment so an employee has exactly one active shift (BR-2).
    /// Gated by Attendance.Shift.Manage.
    /// </summary>
    [HttpPost("shifts/{id:guid}/assign")]
    [RequirePermission("Attendance.Shift.Manage")]
    [ProducesResponseType(typeof(ApiResponse<AssignmentResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignShift(
        [FromRoute] Guid id, [FromBody] AssignShiftRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AssignShiftCommand(id, request.EmployeeIds, request.EffectiveFrom), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<AssignmentResultDto>.Ok(result.Value!, "Shift assigned."));
    }

    /// <summary>
    /// GET /api/v1/attendance/employees/{employeeId}/shift?date=yyyy-MM-dd
    /// Resolves the shift effective for an employee on the given date (defaults to today), resolving
    /// rotation and falling back to the tenant default when no assignment exists (US-ATT-005 FR-5/FR-7).
    /// Gated by Attendance.Shift.Manage.
    /// </summary>
    [HttpGet("employees/{employeeId:guid}/shift")]
    [RequirePermission("Attendance.Shift.Manage")]
    [ProducesResponseType(typeof(ApiResponse<ResolvedShiftDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEmployeeShift(
        [FromRoute] Guid employeeId,
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        var resolveDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await _mediator.Send(
            new GetEmployeeShiftQuery(employeeId, resolveDate), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<ResolvedShiftDto>.Ok(result.Value!));
    }

    // ══════════════════════════════════════════════════════════════
    //  US-ATT-006: Overtime tracking and approval
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/v1/attendance/overtime/pre-approval
    /// Submits an overtime pre-approval request for the acting employee (US-ATT-006 AC-2/FR-4): a
    /// PRE_APPROVED, PENDING record. Returns 400 (past/invalid date or hours), 403 (no employee /
    /// inactive). Gated by Attendance.CheckIn — the same self permission as clock-in.
    /// </summary>
    [HttpPost("overtime/pre-approval")]
    [RequirePermission("Attendance.CheckIn")]
    [ProducesResponseType(typeof(ApiResponse<OvertimeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SubmitOvertimePreApproval(
        [FromBody] OvertimePreApprovalRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SubmitOvertimePreApprovalCommand(request), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<OvertimeDto>.Ok(result.Value!, "Overtime pre-approval submitted."));
    }

    /// <summary>
    /// GET /api/v1/attendance/overtime/my
    /// Returns the acting employee's own overtime records, newest first (US-ATT-006). Gated by
    /// Attendance.CheckIn.
    /// </summary>
    [HttpGet("overtime/my")]
    [RequirePermission("Attendance.CheckIn")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OvertimeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyOvertime(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyOvertimeQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<IReadOnlyList<OvertimeDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/attendance/overtime/pending
    /// Returns the acting manager's pending overtime-approval queue — PENDING records from their direct
    /// reports with employee name/photo and submitted-on (US-ATT-006 AC-3/FR-5). Gated by
    /// Attendance.Approve.Team.
    /// </summary>
    [HttpGet("overtime/pending")]
    [RequirePermission("Attendance.Approve.Team")]
    [ProducesResponseType(typeof(ApiResponse<OvertimeQueueResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPendingOvertime(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPendingOvertimeQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OvertimeQueueResult>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/attendance/overtime/{id}/approve
    /// Approves an overtime record from one of the acting manager's direct reports (US-ATT-006
    /// AC-4/FR-6/FR-7). Sets status APPROVED, approved_minutes (defaults to overtime_minutes or the
    /// adjusted value), flags payroll-ready, and records an immutable history entry — atomically. Blocks
    /// self-approval (BR-8, 403 "self_approval"). Returns 400 (approvedMinutes out of range), 403
    /// ("self_approval"/"not_team_member"), 404 (not found), 409 ("already_actioned"). Gated by
    /// Attendance.Approve.Team.
    /// </summary>
    [HttpPost("overtime/{id:guid}/approve")]
    [RequirePermission("Attendance.Approve.Team")]
    [ProducesResponseType(typeof(ApiResponse<OvertimeDecisionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveOvertime(
        [FromRoute] Guid id,
        [FromBody] ApproveOvertimeRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ApproveOvertimeCommand(id, request?.ApprovedMinutes, request?.Comment), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OvertimeDecisionDto>.Ok(result.Value!, "Overtime approved."));
    }

    /// <summary>
    /// POST /api/v1/attendance/overtime/{id}/reject
    /// Rejects an overtime record from one of the acting manager's direct reports (US-ATT-006). Sets
    /// status REJECTED and records the mandatory reason (min 10 chars) in an immutable history entry.
    /// Same authorization/immutability/self-approval rules as approve. Gated by Attendance.Approve.Team.
    /// </summary>
    [HttpPost("overtime/{id:guid}/reject")]
    [RequirePermission("Attendance.Approve.Team")]
    [ProducesResponseType(typeof(ApiResponse<OvertimeDecisionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectOvertime(
        [FromRoute] Guid id,
        [FromBody] RejectOvertimeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RejectOvertimeCommand(id, request.Reason), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OvertimeDecisionDto>.Ok(result.Value!, "Overtime rejected."));
    }

    /// <summary>
    /// GET /api/v1/attendance/overtime/report?month=yyyy-MM
    /// Returns the monthly HR overtime report: per-employee approved/pending/rejected minutes plus
    /// tenant-wide totals for the selected month (US-ATT-006 AC-5). Gated by Attendance.View.All — the
    /// HR attendance read permission.
    /// </summary>
    [HttpGet("overtime/report")]
    [RequirePermission("Attendance.View.All")]
    [ProducesResponseType(typeof(ApiResponse<OvertimeReportResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetOvertimeReport(
        [FromQuery] string? month, CancellationToken cancellationToken)
    {
        // month is "yyyy-MM"; default to the current UTC month when omitted.
        int year, mon;
        if (string.IsNullOrWhiteSpace(month))
        {
            var now = DateTime.UtcNow;
            year = now.Year; mon = now.Month;
        }
        else if (!TryParseMonth(month, out year, out mon))
        {
            return StatusCode(400, ApiResponse.Fail(
                "Month must be in the format yyyy-MM.", "invalid_month"));
        }

        var result = await _mediator.Send(new GetOvertimeReportQuery(year, mon), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<OvertimeReportResult>.Ok(result.Value!));
    }

    private static bool TryParseMonth(string value, out int year, out int month)
    {
        year = 0; month = 0;
        var parts = value.Split('-');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out year) || !int.TryParse(parts[1], out month)) return false;
        return year is >= 1 and <= 9999 && month is >= 1 and <= 12;
    }

    // ══════════════════════════════════════════════════════════════
    //  US-ATT-007: Monthly attendance summary per employee
    //  All gated by Attendance.View.All — the HR attendance-read permission (the story names
    //  Attendance.Read.All, which is not a catalog entry; reusing the established View.All that the
    //  US-ATT-006 overtime report already uses for the same HR persona).
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// GET /api/v1/attendance/summary/monthly?month=yyyy-MM&amp;departmentId=&amp;locationId=&amp;shiftId=&amp;status=
    /// HR monthly attendance summary: one row per employee for the month with present/absent/late/early/
    /// overtime/leave/holiday/weekly-off/LOP, plus a banner (total employees, average attendance %, total
    /// LOP). Generates on-demand if not yet materialized (US-ATT-007 AC-1/AC-3/AC-5/FR-5).
    /// </summary>
    [HttpGet("summary/monthly")]
    [RequirePermission("Attendance.View.All")]
    [ProducesResponseType(typeof(ApiResponse<MonthlySummaryResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMonthlySummary(
        [FromQuery] string? month,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? locationId,
        [FromQuery] Guid? shiftId,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        if (!ResolveMonth(month, out var year, out var mon, out var error))
            return error!;

        var filter = new MonthlySummaryFilter
        {
            DepartmentId = departmentId,
            LocationId = locationId,
            ShiftId = shiftId,
            Status = status,
        };

        var result = await _mediator.Send(
            new GetMonthlySummaryQuery(year, mon, filter), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<MonthlySummaryResult>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/attendance/summary/monthly/{employeeId}?month=yyyy-MM
    /// Day-by-day attendance breakdown for one employee for the month (US-ATT-007 AC-2 drill-down).
    /// </summary>
    [HttpGet("summary/monthly/{employeeId:guid}")]
    [RequirePermission("Attendance.View.All")]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDailyBreakdownResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEmployeeMonthlyBreakdown(
        [FromRoute] Guid employeeId,
        [FromQuery] string? month,
        CancellationToken cancellationToken)
    {
        if (!ResolveMonth(month, out var year, out var mon, out var error))
            return error!;

        var result = await _mediator.Send(
            new GetEmployeeMonthlyBreakdownQuery(employeeId, year, mon), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<EmployeeDailyBreakdownResult>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/attendance/summary/monthly/generate?month=yyyy-MM
    /// Triggers on-demand (re)generation of the monthly summary for the month (US-ATT-007 AC-3/FR-4).
    /// Returns the resulting generation status (COMPLETED with a generatedAt timestamp).
    /// </summary>
    [HttpPost("summary/monthly/generate")]
    [RequirePermission("Attendance.View.All")]
    [ProducesResponseType(typeof(ApiResponse<SummaryGenerationStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateMonthlySummary(
        [FromQuery] string? month, CancellationToken cancellationToken)
    {
        if (!ResolveMonth(month, out var year, out var mon, out var error))
            return error!;

        var result = await _mediator.Send(
            new GenerateMonthlySummaryCommand(year, mon), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        return Ok(ApiResponse<SummaryGenerationStatusDto>.Ok(result.Value!, "Summary generated."));
    }

    /// <summary>
    /// GET /api/v1/attendance/summary/monthly/export?month=yyyy-MM&amp;format=csv|xlsx|pdf&amp;departmentId=&amp;locationId=&amp;shiftId=
    /// Exports the monthly summary as a file download (US-ATT-007 AC-4/FR-6). Datasets &gt; 1,000
    /// employees route to a Hangfire background job (FR-7) and return 202 Accepted with the job id
    /// (notification deferred — US-NTF).
    /// </summary>
    [HttpGet("summary/monthly/export")]
    [RequirePermission("Attendance.View.All")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MonthlySummaryExportResult>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportMonthlySummary(
        [FromQuery] string? month,
        [FromQuery] string? format,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? locationId,
        [FromQuery] Guid? shiftId,
        CancellationToken cancellationToken)
    {
        if (!ResolveMonth(month, out var year, out var mon, out var error))
            return error!;

        var filter = new MonthlySummaryFilter
        {
            DepartmentId = departmentId,
            LocationId = locationId,
            ShiftId = shiftId,
        };

        var result = await _mediator.Send(
            new ExportMonthlySummaryQuery(year, mon, format ?? "csv", filter), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400,
                ApiResponse.Fail(result.Error!, result.ErrorCode));

        var export = result.Value!;
        if (export.Queued)
            return StatusCode(StatusCodes.Status202Accepted,
                ApiResponse<MonthlySummaryExportResult>.Ok(export,
                    "Export is large and is being generated in the background."));

        return File(export.FileContent!, export.ContentType!, export.FileName!);
    }

    /// <summary>
    /// Parses the "yyyy-MM" month query param (default = current UTC month). On failure, sets
    /// <paramref name="error"/> to a 400 result and returns false.
    /// </summary>
    private bool ResolveMonth(string? month, out int year, out int monthNum, out IActionResult? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(month))
        {
            var now = DateTime.UtcNow;
            year = now.Year; monthNum = now.Month;
            return true;
        }

        if (!TryParseMonth(month, out year, out monthNum))
        {
            error = StatusCode(400, ApiResponse.Fail("Month must be in the format yyyy-MM.", "invalid_month"));
            return false;
        }
        return true;
    }
}
