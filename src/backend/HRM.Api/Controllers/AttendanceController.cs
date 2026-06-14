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
}
