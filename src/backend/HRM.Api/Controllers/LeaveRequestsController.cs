using HRM.Application.DTOs;
using HRM.Application.Features.LeaveRequests.Commands;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Application.Features.LeaveRequests.Queries;
using HRM.Infrastructure.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRM.Api.Controllers;

/// <summary>
/// Employee leave-request endpoints (US-LV-003).
/// Employees apply for leave and view their own requests. Tenant-scoped via the
/// resolved tenant context; the acting employee is resolved from the JWT.
/// </summary>
[ApiController]
[Route("api/v1/leaves")]
[Authorize]
public sealed class LeaveRequestsController : ControllerBase
{
    private readonly IMediator _mediator;

    public LeaveRequestsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// POST /api/v1/leaves
    /// Submits a new leave request for the current employee (FR-5, AC-1..AC-6).
    /// </summary>
    [HttpPost]
    [RequirePermission("Leave.Apply")]
    [ProducesResponseType(typeof(ApiResponse<LeaveRequestDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateLeaveRequestRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateLeaveRequestCommand(
            request.LeaveTypeId,
            request.StartDate,
            request.EndDate,
            request.IsHalfDay,
            request.HalfDaySession,
            request.Reason,
            request.Attachments);

        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return StatusCode(StatusCodes.Status201Created,
            ApiResponse<LeaveRequestDto>.Ok(result.Value!, "Leave request submitted."));
    }

    /// <summary>
    /// GET /api/v1/leaves/mine
    /// Lists the current employee's own leave requests, newest first.
    /// </summary>
    [HttpGet("mine")]
    [RequirePermission("Leave.View.Own")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LeaveRequestDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMine(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyLeaveRequestsQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<LeaveRequestDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/leaves/balance-preview
    /// Returns the inline balance preview for a leave type and optional date range (FR-2, AC-2).
    /// </summary>
    [HttpGet("balance-preview")]
    [RequirePermission("Leave.View.Own")]
    [ProducesResponseType(typeof(ApiResponse<LeaveBalancePreviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBalancePreview(
        [FromQuery] Guid leaveTypeId,
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] bool isHalfDay,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetLeaveBalancePreviewQuery(leaveTypeId, startDate, endDate, isHalfDay), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LeaveBalancePreviewDto>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/leaves/my-balance?year={year}
    /// Returns the current employee's leave-balance dashboard: one summary per active leave type
    /// (entitlement, used, pending, balance, carry-forward, expired) for the leave year
    /// (defaults to the current leave year). Deactivated types that still carry a balance are
    /// flagged IsArchived (US-LV-006 FR-1/FR-2, AC-1/AC-5, BR-1..BR-4).
    /// </summary>
    [HttpGet("my-balance")]
    [RequirePermission("Leave.View.Own")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LeaveBalanceDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyBalance(
        [FromQuery] int? year, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyLeaveBalanceQuery(year), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<LeaveBalanceDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/leaves/my-ledger?leaveTypeId={id}&amp;year={year}
    /// Returns the current employee's full ledger transaction log (accruals, usages, adjustments,
    /// carry-forwards, expirations) for one leave type and year, ordered chronologically
    /// (US-LV-006 FR-3, AC-2).
    /// </summary>
    [HttpGet("my-ledger")]
    [RequirePermission("Leave.View.Own")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LeaveLedgerEntryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyLedger(
        [FromQuery] Guid leaveTypeId,
        [FromQuery] int? year,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyLeaveLedgerQuery(leaveTypeId, year), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<LeaveLedgerEntryDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/leaves/my-upcoming
    /// Returns the current employee's Approved + Pending future leaves (start_date &gt;= today),
    /// ordered by start date (US-LV-006 FR-4, AC-3).
    /// </summary>
    [HttpGet("my-upcoming")]
    [RequirePermission("Leave.View.Own")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UpcomingLeaveDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyUpcoming(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyUpcomingLeavesQuery(), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<IReadOnlyList<UpcomingLeaveDto>>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/leaves/pending
    /// Returns the current manager's pending-approval queue: pending requests from their
    /// direct reports, with inline balance, overdue flag, and team-conflict count
    /// (US-LV-004 FR-1..FR-5, AC-1..AC-3). Supports filtering, sorting, and server-side paging.
    /// </summary>
    [HttpGet("pending")]
    [RequirePermission("Leave.Approve.Team")]
    [ProducesResponseType(typeof(ApiResponse<PendingLeaveQueueResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPending(
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] Guid? employeeId,
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] string? sortBy,
        [FromQuery] bool sortAscending = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new PendingLeaveQueueQueryParams
        {
            LeaveTypeId = leaveTypeId,
            EmployeeId = employeeId,
            StartDate = startDate,
            EndDate = endDate,
            SortBy = sortBy,
            SortAscending = sortAscending,
            Page = page,
            PageSize = pageSize,
        };

        var result = await _mediator.Send(
            new GetPendingLeaveRequestsQuery(queryParams), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<PendingLeaveQueueResult>.Ok(result.Value!));
    }

    /// <summary>
    /// GET /api/v1/leaves/team-calendar?from={date}&amp;to={date}
    /// Returns the team leave calendar for the current caller (US-LV-009 FR-1..FR-4, FR-6/FR-7).
    /// Row-level access is resolved by the handler from the caller's permissions/reporting line:
    /// HR (Leave.View.All) sees the whole org; a manager sees their direct reports' Approved +
    /// Pending leaves with full detail; a regular employee sees only their department colleagues'
    /// Approved leaves with leave-type/status detail suppressed (BR-1). Cancelled/Rejected are
    /// excluded (BR-4). Public holidays in the range are returned for background highlights (FR-7).
    ///
    /// Authorized for any authenticated employee — only the class-level [Authorize] applies; the
    /// handler performs the row-level scoping. The status filter (FR-6) is honoured for manager/HR
    /// scope only and ignored for employee scope.
    /// </summary>
    [HttpGet("team-calendar")]
    [ProducesResponseType(typeof(ApiResponse<TeamLeaveCalendarDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTeamCalendar(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? employeeId,
        [FromQuery] Guid? leaveTypeId,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var queryParams = new TeamLeaveCalendarQueryParams
        {
            From = from,
            To = to,
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            Status = status,
        };

        var result = await _mediator.Send(
            new GetTeamLeaveCalendarQuery(queryParams), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<TeamLeaveCalendarDto>.Ok(result.Value!));
    }

    /// <summary>
    /// POST /api/v1/leaves/{id}/approve
    /// Approves a pending leave request from one of the current manager's direct reports
    /// (US-LV-005 FR-1, AC-1/AC-3/AC-5). Sets the status to Approved, deducts the balance via a
    /// LeaveLedger "Used" entry, and records approval history. Comment is optional (BR-2).
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [RequirePermission("Leave.Approve.Team")]
    [ProducesResponseType(typeof(ApiResponse<LeaveApprovalResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(
        [FromRoute] Guid id,
        [FromBody] ApproveLeaveRequestRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ApproveLeaveRequestCommand(id, request?.Comment), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LeaveApprovalResultDto>.Ok(result.Value!, "Leave request approved."));
    }

    /// <summary>
    /// POST /api/v1/leaves/{id}/reject
    /// Rejects a pending leave request from one of the current manager's direct reports
    /// (US-LV-005 FR-2, AC-2/AC-5). Sets the status to Rejected and records approval history;
    /// no balance is deducted. The rejection reason is mandatory (BR-2).
    /// </summary>
    [HttpPost("{id:guid}/reject")]
    [RequirePermission("Leave.Approve.Team")]
    [ProducesResponseType(typeof(ApiResponse<LeaveApprovalResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        [FromRoute] Guid id,
        [FromBody] RejectLeaveRequestRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RejectLeaveRequestCommand(id, request.Reason), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LeaveApprovalResultDto>.Ok(result.Value!, "Leave request rejected."));
    }

    /// <summary>
    /// POST /api/v1/leaves/{id}/cancel
    /// Cancels the current employee's OWN leave request (US-LV-010 FR-1, AC-1..AC-4). For a pending
    /// request the status becomes Cancelled with no balance impact (AC-1); for an approved future
    /// request a reversal ledger entry restores the balance (AC-2). The reason is mandatory when
    /// cancelling an approved leave (BR-5). An approved leave that has already started/passed is
    /// blocked (AC-3). Only the requesting employee may cancel — the ownership check is enforced in
    /// the handler regardless of the gating permission (BR-1).
    ///
    /// Gated by Leave.View.Own — cancellation is a self-service action over the employee's own
    /// request, consistent with the other /leaves self endpoints (/mine, /balance-preview, my-*).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [RequirePermission("Leave.View.Own")]
    [ProducesResponseType(typeof(ApiResponse<LeaveCancellationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        [FromRoute] Guid id,
        [FromBody] CancelLeaveRequestRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CancelLeaveRequestCommand(id, request?.Reason), cancellationToken);

        if (result.IsFailure)
            return StatusCode(result.StatusCode ?? 400, ApiResponse.Fail(result.Error!));

        return Ok(ApiResponse<LeaveCancellationResultDto>.Ok(result.Value!, "Leave request cancelled."));
    }
}
