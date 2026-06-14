namespace HRM.Application.Features.LeaveRequests.DTOs;

/// <summary>
/// Request body for POST /api/v1/leaves (US-LV-003 FR-5).
/// Attachments are passed as already-uploaded URLs; blob upload is out of scope (NFR-3, deferred).
/// </summary>
public sealed record CreateLeaveRequestRequest
{
    public Guid LeaveTypeId { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public bool IsHalfDay { get; init; }
    /// <summary>"AM" or "PM" when IsHalfDay is true; otherwise null.</summary>
    public string? HalfDaySession { get; init; }
    public string? Reason { get; init; }
    /// <summary>URLs of already-uploaded attachments (max 3, PDF/JPG/PNG per §10).</summary>
    public IReadOnlyList<string>? Attachments { get; init; }
    /// <summary>
    /// US-LV-011 AC-1: when the balance is insufficient and negative balance is not allowed, the API
    /// returns a signal that the request can be processed as Loss of Pay (LOP). If the employee
    /// re-submits with this flag set, the request is created as LOP (is_lop = true, no balance
    /// deduction) instead of being hard-blocked.
    /// </summary>
    public bool ConfirmLop { get; init; }
}

/// <summary>
/// DTO returned for a leave request (US-LV-003).
/// </summary>
public sealed record LeaveRequestDto
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid LeaveTypeId { get; init; }
    public string LeaveTypeName { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public bool IsHalfDay { get; init; }
    public string? HalfDaySession { get; init; }
    public decimal TotalDays { get; init; }
    public string? Reason { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; }
    public IReadOnlyList<string> Attachments { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    /// <summary>US-LV-011 FR-4: true when this request is a Loss-of-Pay (LOP) entry (no balance deducted).</summary>
    public bool IsLop { get; init; }
    /// <summary>US-LV-011 FR-4: origin of the LOP entry (EmployeeRequest/SystemGenerated/HrAssigned/Compulsory); null for normal leave.</summary>
    public string? LopSource { get; init; }
}

/// <summary>
/// Inline balance preview for the leave-application form (US-LV-003 FR-2, AC-2).
/// </summary>
public sealed record LeaveBalancePreviewDto
{
    public Guid EmployeeId { get; init; }
    public Guid LeaveTypeId { get; init; }
    public string LeaveTypeName { get; init; } = string.Empty;
    public int LeaveYear { get; init; }
    /// <summary>Current balance from the leave ledger running total.</summary>
    public decimal CurrentBalance { get; init; }
    /// <summary>Working days requested for the supplied date range (0.5 for a half-day).</summary>
    public decimal RequestedDays { get; init; }
    /// <summary>Balance after the request would be deducted (CurrentBalance - RequestedDays).</summary>
    public decimal ProjectedBalance { get; init; }
}

/// <summary>
/// One row in a manager's pending-leave-approval queue (US-LV-004 FR-2).
/// The current leave balance, overdue flag, and team-conflict count are computed
/// per-request by the service.
/// </summary>
public sealed record PendingLeaveRequestDto
{
    public Guid RequestId { get; init; }
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    /// <summary>Employee profile photo URL (Employee.ProfilePhotoUrl); null if none.</summary>
    public string? EmployeePhoto { get; init; }
    public Guid LeaveTypeId { get; init; }
    public string LeaveTypeName { get; init; } = string.Empty;
    /// <summary>Hex colour of the leave type (e.g. "#4CAF50"); null if not configured.</summary>
    public string? LeaveTypeColor { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public decimal TotalDays { get; init; }
    public string? Reason { get; init; }
    /// <summary>True when the request has one or more supporting attachments (FR-2).</summary>
    public bool HasAttachments { get; init; }
    /// <summary>Real-time balance for this employee + leave type from the ledger (BR-4).</summary>
    public decimal CurrentBalance { get; init; }
    public DateTime RequestedAt { get; init; }
    /// <summary>True when the request has been pending &gt; 30 days without action (BR-3).</summary>
    public bool IsOverdue { get; init; }
    /// <summary>
    /// Number of this employee's team-mates (same manager's direct reports) with an
    /// Approved leave overlapping this request's date range (FR-5).
    /// </summary>
    public int TeamConflictCount { get; init; }
}

/// <summary>
/// Server-side paged result for the pending-leave queue (US-LV-004 FR-4, AC-2).
/// Mirrors the EmployeeListResult envelope (Items + TotalCount + Page + PageSize).
/// </summary>
public sealed record PendingLeaveQueueResult
{
    public IReadOnlyList<PendingLeaveRequestDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

/// <summary>
/// Request body for POST /api/v1/leaves/{id}/approve (US-LV-005 FR-1). Comment is optional (BR-2).
/// </summary>
public sealed record ApproveLeaveRequestRequest
{
    /// <summary>Optional approval comment shown to the employee (BR-2).</summary>
    public string? Comment { get; init; }
}

/// <summary>
/// Request body for POST /api/v1/leaves/{id}/reject (US-LV-005 FR-2). Reason is mandatory (BR-2).
/// </summary>
public sealed record RejectLeaveRequestRequest
{
    /// <summary>Mandatory rejection reason shown to the employee (BR-2, AC-2).</summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Request body for POST /api/v1/leaves/{id}/cancel (US-LV-010 FR-1).
/// The reason is mandatory for approved leaves (BR-5) and optional for pending ones; the
/// conditional rule is enforced by the service, which knows the request's current status.
/// </summary>
public sealed record CancelLeaveRequestRequest
{
    /// <summary>Cancellation reason. Required when cancelling an approved leave (BR-5).</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Result of an employee self-cancellation (US-LV-010 AC-1, AC-2). Returns the new status and,
/// for an approved cancellation, the reversal ledger entry that restored the balance.
/// </summary>
public sealed record LeaveCancellationResultDto
{
    public Guid RequestId { get; init; }
    /// <summary>New request status: always "Cancelled".</summary>
    public string Status { get; init; } = string.Empty;
    /// <summary>
    /// The id of the LeaveLedger "Adjusted" reversal entry written for an approved cancellation;
    /// null for a pending cancellation (nothing had been deducted, AC-1).
    /// </summary>
    public Guid? LedgerEntryId { get; init; }
    /// <summary>
    /// The restored balance after the reversal entry for an approved cancellation; null for a
    /// pending cancellation.
    /// </summary>
    public decimal? BalanceAfter { get; init; }
    /// <summary>When the cancellation was recorded.</summary>
    public DateTime CancelledAt { get; init; }
}

/// <summary>
/// Result of an approve/reject decision on a leave request (US-LV-005 AC-1, AC-2).
/// Returns the new status and, for an approval, the ledger entry that recorded the deduction.
/// </summary>
public sealed record LeaveApprovalResultDto
{
    public Guid RequestId { get; init; }
    /// <summary>New request status: "Approved" or "Rejected".</summary>
    public string Status { get; init; } = string.Empty;
    /// <summary>The action recorded in approval history: "Approved" or "Rejected".</summary>
    public string Action { get; init; } = string.Empty;
    /// <summary>Approval level the decision was recorded at (always 1 in this story).</summary>
    public int ApprovalLevel { get; init; }
    /// <summary>The id of the LeaveLedger "Used" entry written on approval; null on rejection.</summary>
    public Guid? LedgerEntryId { get; init; }
    /// <summary>The balance after deduction on approval; null on rejection.</summary>
    public decimal? BalanceAfter { get; init; }
    /// <summary>When the decision was taken.</summary>
    public DateTime ActionedAt { get; init; }
}

/// <summary>
/// Filter/sort/paging parameters for the pending-leave queue (US-LV-004 FR-3, FR-4).
/// </summary>
public sealed record PendingLeaveQueueQueryParams
{
    /// <summary>Optional filter: only requests of this leave type.</summary>
    public Guid? LeaveTypeId { get; init; }
    /// <summary>Optional filter: only requests from this employee.</summary>
    public Guid? EmployeeId { get; init; }
    /// <summary>Optional filter: only requests whose period ends on/after this date.</summary>
    public DateOnly? StartDate { get; init; }
    /// <summary>Optional filter: only requests whose period starts on/before this date.</summary>
    public DateOnly? EndDate { get; init; }
    /// <summary>Sort field: "requestedAt" (default) or "startDate".</summary>
    public string? SortBy { get; init; }
    /// <summary>True (default) for ascending (oldest first per AC-1); false for descending.</summary>
    public bool SortAscending { get; init; } = true;
    /// <summary>1-based page number (default 1).</summary>
    public int Page { get; init; } = 1;
    /// <summary>Page size (default 20, capped at 50 per §10).</summary>
    public int PageSize { get; init; } = 20;
}
