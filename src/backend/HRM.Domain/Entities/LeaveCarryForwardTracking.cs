namespace HRM.Domain.Entities;

/// <summary>
/// Tracks a single carry-forward of unused leave from one leave year to the next, and the
/// subsequent expiry of any carried days that remain unconsumed past their expiry date
/// (US-LV-008 §7, FR-2, FR-3, BR-3, BR-4).
///
/// One row per (tenant, employee, leave_type, from_year → to_year). The unique index on that
/// tuple makes the year-end job idempotent (NFR-3): a second run finds the existing row and skips.
///
/// FIFO consumption (BR-4) is derived — not stored as a running counter — from the new-year
/// "Used" ledger entries vs. <see cref="CarriedDays"/>; see LeaveCarryForwardCalculator.
/// </summary>
public sealed class LeaveCarryForwardTracking : BaseEntity
{
    /// <summary>FK to the employee whose balance was carried forward.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>FK to the leave type being carried forward.</summary>
    public Guid LeaveTypeId { get; set; }

    /// <summary>The closing leave year the unused balance came from.</summary>
    public int FromYear { get; set; }

    /// <summary>The new leave year the balance was carried into (= FromYear + 1).</summary>
    public int ToYear { get; set; }

    /// <summary>Days actually carried forward = MIN(unused_balance, carry_forward_limit) (BR-1).</summary>
    public decimal CarriedDays { get; set; }

    /// <summary>
    /// Date the carried days expire (BR-3): first day of the new leave year +
    /// carry_forward_expiry_months. Null when the leave type's expiry months is null
    /// (carried days never expire).
    /// </summary>
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>
    /// Days that have been expired so far for this carry-forward (FR-3). Zero until the expiry
    /// job runs; set to the remaining unconsumed carried days once the expiry date has passed.
    /// </summary>
    public decimal ExpiredDays { get; set; }

    /// <summary>
    /// Lifecycle state of this carry-forward. One of the
    /// <see cref="CarryForwardTrackingStatus"/> constants.
    /// </summary>
    public string Status { get; set; } = CarryForwardTrackingStatus.Active;

    // ── Navigation ─────────────────────────────────────────────────

    public Employee? Employee { get; set; }
    public LeaveType? LeaveType { get; set; }
}

/// <summary>
/// Status values for <see cref="LeaveCarryForwardTracking"/>. Stored as a short string column
/// (mirrors how other leave enums are persisted as text).
/// </summary>
public static class CarryForwardTrackingStatus
{
    /// <summary>Carried days exist and (some) remain unexpired.</summary>
    public const string Active = "Active";

    /// <summary>The expiry date passed and the remaining carried days were expired.</summary>
    public const string Expired = "Expired";

    /// <summary>All carried days were consumed before the expiry date (nothing left to expire).</summary>
    public const string Consumed = "Consumed";
}
