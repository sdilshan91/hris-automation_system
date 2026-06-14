using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service for employee leave-request submission and retrieval (US-LV-003).
/// All operations are tenant-scoped via ITenantContext and resolve the acting
/// employee from the current user. Balance is read from the LeaveLedger running total.
/// </summary>
public interface ILeaveRequestService
{
    /// <summary>
    /// Creates a Pending leave request for the current employee, enforcing
    /// FR-3..FR-6, AC-1..AC-6, and BR-1..BR-6 (where supported).
    /// </summary>
    Task<Result<LeaveRequestDto>> CreateAsync(
        CreateLeaveRequestRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the current employee's own leave requests, newest first (FR-1, "My Leaves").
    /// </summary>
    Task<Result<IReadOnlyList<LeaveRequestDto>>> GetMineAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the inline balance preview for a leave type and (optional) date range (FR-2, AC-2).
    /// </summary>
    Task<Result<LeaveBalancePreviewDto>> GetBalancePreviewAsync(
        Guid leaveTypeId,
        DateOnly? startDate,
        DateOnly? endDate,
        bool isHalfDay,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the pending leave requests of the current manager's direct reports (US-LV-004,
    /// FR-1..FR-5, BR-1), tenant-scoped, with inline balance, overdue flag, and team-conflict
    /// count, filtered/sorted/paged. If the current user has no employee record, returns an
    /// empty page (does not fail).
    /// </summary>
    Task<Result<PendingLeaveQueueResult>> GetPendingForManagerAsync(
        PendingLeaveQueueQueryParams queryParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the team leave calendar for the current caller (US-LV-009 FR-1..FR-4, FR-6/FR-7),
    /// tenant-scoped. Row-level scope is resolved from the caller's permissions/reporting line:
    ///   - Leave.View.All (HR): all employees' Approved + Pending leaves, full detail.
    ///   - Manager (has direct reports): direct reports' Approved + Pending, full detail.
    ///   - Employee: own-department colleagues' Approved leaves only, with leave-type/status
    ///     detail suppressed (BR-1 — "just on leave").
    /// Cancelled and Rejected leaves are excluded (BR-4). Public holidays in the range are
    /// included for background highlights (FR-7). If the current user has no employee record,
    /// returns an empty calendar (does not fail).
    /// </summary>
    Task<Result<TeamLeaveCalendarDto>> GetTeamLeaveCalendarAsync(
        TeamLeaveCalendarQueryParams queryParams,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a pending leave request from one of the current manager's direct reports
    /// (US-LV-005 FR-1/FR-3, AC-1/AC-3/AC-5, BR-1/BR-3/BR-5). Sets the status to Approved,
    /// appends a LeaveLedger "Used" deduction, and records a LeaveApprovalHistory row.
    /// Returns 403 if the caller is not the request's approver, 404 if the request is not found,
    /// 400/409 if it has already been actioned or the balance is insufficient, and 409 on a
    /// concurrent-action conflict (xmin). Comment is optional.
    /// </summary>
    Task<Result<LeaveApprovalResultDto>> ApproveAsync(
        Guid leaveRequestId,
        string? comment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a pending leave request from one of the current manager's direct reports
    /// (US-LV-005 FR-2/FR-4, AC-2/AC-5, BR-1/BR-2/BR-3). Sets the status to Rejected and records
    /// a LeaveApprovalHistory row; writes no ledger entry. The reason is mandatory (BR-2).
    /// Returns 403/404/400/409 under the same conditions as <see cref="ApproveAsync"/>.
    /// </summary>
    Task<Result<LeaveApprovalResultDto>> RejectAsync(
        Guid leaveRequestId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the current employee's OWN leave request (US-LV-010 FR-1..FR-7, AC-1..AC-4,
    /// BR-1..BR-5). Only the requesting employee may cancel their own leave; managers cannot
    /// cancel on behalf (BR-1 → 403). For a pending request the status is set to Cancelled with
    /// no ledger impact (AC-1). For an approved request the status is set to Cancelled, a
    /// LeaveLedger "Adjusted" reversal entry (positive days) restores the balance (AC-2), and the
    /// reason is mandatory (BR-5). An approved leave that has already started/passed is blocked
    /// (AC-3, BR-3). Rejected/already-cancelled leaves cannot be cancelled (BR-2). Returns
    /// 403/404/400 accordingly, and 409 on a concurrent-action conflict (xmin, NFR-3).
    /// </summary>
    Task<Result<LeaveCancellationResultDto>> CancelAsync(
        Guid leaveRequestId,
        string? reason,
        CancellationToken cancellationToken = default);
}
