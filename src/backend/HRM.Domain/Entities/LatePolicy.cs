namespace HRM.Domain.Entities;

/// <summary>
/// Valid <see cref="LatePolicy.Period"/> values (US-ATT-008 §7).
/// </summary>
public static class LatePolicyPeriod
{
    /// <summary>The deduction threshold is evaluated per calendar month.</summary>
    public const string Monthly = "MONTHLY";

    /// <summary>The deduction threshold is evaluated per calendar quarter.</summary>
    public const string Quarterly = "QUARTERLY";

    public static bool IsValid(string period) => period is Monthly or Quarterly;
}

/// <summary>
/// Per-tenant late-arrival policy (US-ATT-008 §7, FR-4). Exactly one row per tenant; created lazily
/// with safe defaults when first read. Drives the late-deduction rule that feeds LOP in the monthly
/// summary (BR-4/AC-4) and the chronic-lateness HR-escalation threshold (FR-7). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter (no RLS — same as the rest of the
/// module). Maps to the "late_policy" table.
/// </summary>
public sealed class LatePolicy : BaseEntity
{
    /// <summary>Number of late arrivals within <see cref="Period"/> that triggers the deduction (FR-4/BR-4).</summary>
    public int ThresholdCount { get; set; } = 3;

    /// <summary>Days of pay deducted once <see cref="ThresholdCount"/> is reached, e.g. 0.5 (BR-4). decimal(3,1).</summary>
    public decimal DeductionDays { get; set; } = 0.5m;

    /// <summary>Evaluation window: one of <see cref="LatePolicyPeriod"/> (default MONTHLY).</summary>
    public string Period { get; set; } = LatePolicyPeriod.Monthly;

    /// <summary>FR-5: send an in-app notification on each late arrival. Notification delivery is DEFERRED (US-NTF).</summary>
    public bool NotificationOnLate { get; set; }

    /// <summary>FR-7: lates per month above which HR is escalated (chronic). Drives the report's isChronic flag.</summary>
    public int ChronicThreshold { get; set; } = 5;

    /// <summary>Whether the deduction rule is enforced (§10 — deductions are optional per tenant). Default false.</summary>
    public bool IsActive { get; set; }
}
