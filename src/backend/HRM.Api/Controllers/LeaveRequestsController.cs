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
}
