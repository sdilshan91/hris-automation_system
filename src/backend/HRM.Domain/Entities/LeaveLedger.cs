using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// Immutable transaction log for leave balance changes (US-LV-002 FR-5).
/// Records accruals, usage, adjustments, encashments, carry-forwards, and expirations.
/// </summary>
public sealed class LeaveLedger : BaseEntity
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
