using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// Immutable transaction log for leave balance changes (US-LV-002 FR-5).
/// Records accruals, usage, adjustments, encashments, carry-forwards, and expirations.
/// </summary>
public sealed class LeaveLedger : BaseEntity, IAuditExempt
{
    /// <summary>
    /// Type of ledger entry.
    /// </summary>
    public LedgerEntryType EntryType { get; set; }

    /// <summary>
    /// FK to employee.
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// FK to leave type.
    /// </summary>
    public Guid LeaveTypeId { get; set; }

    /// <summary>
    /// Leave year this entry belongs to.
    /// </summary>
    public int LeaveYear { get; set; }

    /// <summary>
    /// BUG-291: which accrual PERIOD within the leave year this Accrual credit is for (1-based), so a
    /// frequency-aware accrual (Monthly = 12 periods, Quarterly = 4, Yearly/Upfront = 1) credits each period
    /// exactly once. This is the granularity the accrual idempotency guard keys on — before BUG-291 the guard
    /// was year-scoped, so the first run of the year credited the whole 12/12 and every later run was skipped,
    /// making a Monthly/Quarterly type behave like Yearly and over-crediting balances that reach encashment /
    /// F&amp;F.
    ///
    /// <para>Set only on <see cref="LedgerEntryType.Accrual"/> rows written by the accrual job. NULL on every
    /// other entry type AND on legacy accrual rows written before BUG-291 (those predate period-tagging and
    /// each already credited the FULL year the old way — a NULL accrual row is treated as "this year is
    /// already fully accrued" so we never double-credit or retroactively re-shape an existing balance).</para>
    /// </summary>
    public int? AccrualPeriod { get; set; }

    /// <summary>
    /// Optional FK to the leave request that produced this entry (US-LV-005 §7).
    /// Set on the "Used" deduction written when a request is approved; null for accruals,
    /// adjustments, carry-forwards, etc. that are not tied to a specific request.
    /// </summary>
    public Guid? LeaveRequestId { get; set; }

    /// <summary>
    /// Amount of days (positive for accrual/carry-forward, negative for used/encashed/expired).
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Running balance after this entry.
    /// </summary>
    public decimal BalanceAfter { get; set; }

    /// <summary>
    /// DF-19 / ISSUE-045: which balance pool this row drew from / restored to. Set only on the
    /// per-pool Used deduction rows written at approval and the matching Adjusted restore rows
    /// written at cancellation. NULL on legacy rows and on non-pool entries (Accrual credits,
    /// CarryForward credits, Encashed draw-downs, Expired forfeitures) — those are not FIFO-split.
    /// </summary>
    public LeavePool? Pool { get; set; }

    /// <summary>
    /// DF-19 / ISSUE-045: the carry-forward bucket this row drew from, set only on
    /// <see cref="LeavePool.CarryForward"/>-pool Used/Adjusted rows. Points at the
    /// <see cref="LeaveCarryForwardTracking"/> whose <c>ConsumedDays</c> this deduction incremented
    /// (and a cancellation decrements). NULL on every other row.
    /// </summary>
    public Guid? CarryForwardTrackingId { get; set; }

    /// <summary>
    /// Human-readable description of this ledger entry.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// When this transaction occurred.
    /// </summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ─────────────────────────────────────────────────

    public Employee? Employee { get; set; }
    public LeaveType? LeaveType { get; set; }
    public LeaveRequest? LeaveRequest { get; set; }
}
