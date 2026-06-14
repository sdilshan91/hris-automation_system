namespace HRM.Domain.Enums;

/// <summary>
/// Marks a leave type as a special system-managed category (US-LV-011 FR-1).
/// Stored as a string on leave_types.system_category. A system type cannot be deleted/deactivated
/// but CAN be renamed (FR-1). Most leave types are <see cref="None"/>.
/// </summary>
public enum LeaveTypeSystemCategory
{
    /// <summary>An ordinary, fully user-managed leave type (the default).</summary>
    None = 0,

    /// <summary>
    /// The Loss-of-Pay (LOP) system leave type (US-LV-011 FR-1, BR-1). Zero entitlement, no balance;
    /// purely a deduction marker consumed by payroll. Auto-created during tenant setup; cannot be
    /// deleted but can be renamed.
    /// </summary>
    LossOfPay = 1,
}
