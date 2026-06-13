namespace HRM.Application.Features.LeaveRequests.DTOs;

/// <summary>
/// One leave-type balance summary card on the employee dashboard (US-LV-006 FR-1/FR-2, AC-1).
/// The balance is computed component-wise from the LeaveLedger (BR-1):
/// <c>Balance = Entitlement + CarryForward - Used - Expired + Adjustments</c>.
/// "Pending" days are shown separately and NOT subtracted from <see cref="Balance"/> (BR-2).
/// </summary>
public sealed record LeaveBalanceDto
{
    public Guid LeaveTypeId { get; init; }
    public string LeaveTypeName { get; init; } = string.Empty;
    /// <summary>Hex colour of the leave type (e.g. "#4CAF50"); null if not configured.</summary>
    public string? Color { get; init; }
    /// <summary>
    /// Effective entitlement resolved by the entitlement engine (override &gt; rule &gt; default),
    /// pro-rated for mid-year joiners (US-LV-002). This is the "granted" allowance for the year.
    /// </summary>
    public decimal Entitlement { get; init; }
    /// <summary>Sum of ledger "Used" deductions for the year (positive magnitude).</summary>
    public decimal Used { get; init; }
    /// <summary>
    /// Sum of total_days of the employee's currently Pending leave requests for this type/year.
    /// Shown separately; NOT deducted from <see cref="Balance"/> until approved (BR-2).
    /// </summary>
    public decimal Pending { get; init; }
    /// <summary>Sum of ledger "CarryForward" credits brought into the year.</summary>
    public decimal CarryForward { get; init; }
    /// <summary>Sum of ledger "Expired" debits for the year (positive magnitude).</summary>
    public decimal Expired { get; init; }
    /// <summary>Sum of ledger "Adjusted" entries (signed; may be positive or negative).</summary>
    public decimal Adjustments { get; init; }
    /// <summary>BR-1: Entitlement + CarryForward - Used - Expired + Adjustments.</summary>
    public decimal Balance { get; init; }
    /// <summary>The leave year this summary is for (BR-4 tenant leave-year boundary).</summary>
    public int LeaveYear { get; init; }
    /// <summary>
    /// True when the leave type has been deactivated but still carries a non-zero balance for the
    /// year — surfaced in the dashboard's collapsed "Archived" section (BR-3). Active types are false.
    /// </summary>
    public bool IsArchived { get; init; }
}

/// <summary>
/// One transaction row in the per-leave-type ledger detail view (US-LV-006 FR-3, AC-2).
/// Mirrors a single <see cref="HRM.Domain.Entities.LeaveLedger"/> entry.
/// </summary>
public sealed record LeaveLedgerEntryDto
{
    public Guid Id { get; init; }
    public Guid LeaveTypeId { get; init; }
    public int LeaveYear { get; init; }
    /// <summary>Entry type: Accrual, Used, Adjusted, Encashed, CarryForward, or Expired.</summary>
    public string EntryType { get; init; } = string.Empty;
    /// <summary>Signed day amount (positive for credits, negative for debits).</summary>
    public decimal Amount { get; init; }
    /// <summary>Running balance after this entry.</summary>
    public decimal BalanceAfter { get; init; }
    public string? Description { get; init; }
    /// <summary>Optional link to the leave request that produced this entry (Used deductions).</summary>
    public Guid? LeaveRequestId { get; init; }
    public DateTime OccurredAt { get; init; }
}

/// <summary>
/// One upcoming leave entry on the dashboard's "Upcoming Leaves" timeline (US-LV-006 FR-4, AC-3).
/// Approved and Pending future requests (start_date &gt;= today).
/// </summary>
public sealed record UpcomingLeaveDto
{
    public Guid RequestId { get; init; }
    public Guid LeaveTypeId { get; init; }
    public string LeaveTypeName { get; init; } = string.Empty;
    public string? LeaveTypeColor { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public decimal TotalDays { get; init; }
    public bool IsHalfDay { get; init; }
    public string? HalfDaySession { get; init; }
    /// <summary>Request status: "Approved" or "Pending".</summary>
    public string Status { get; init; } = string.Empty;
}
