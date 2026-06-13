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
