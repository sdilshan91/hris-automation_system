namespace HRM.Domain.Entities;

/// <summary>
/// Per-employee override for a specific leave type and year (US-LV-002 AC-3).
/// Takes precedence over all rule-based entitlements.
/// </summary>
public sealed class LeaveEntitlementOverride : BaseEntity
{
    /// <summary>
    /// FK to the employee this override applies to.
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// FK to the leave type this override applies to.
    /// </summary>
    public Guid LeaveTypeId { get; set; }

    /// <summary>
    /// Leave year this override applies to (e.g. 2026).
    /// </summary>
    public int LeaveYear { get; set; }

    /// <summary>
    /// Overridden entitlement days.
    /// </summary>
    public decimal EntitlementDays { get; set; }

    /// <summary>
    /// Reason for the override (required for audit).
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    // ── Navigation ─────────────────────────────────────────────────

    public Employee? Employee { get; set; }
    public LeaveType? LeaveType { get; set; }
}
