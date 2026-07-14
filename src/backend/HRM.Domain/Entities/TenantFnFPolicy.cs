namespace HRM.Domain.Entities;

/// <summary>
/// A per-tenant, effective-dated Full-and-Final (F&amp;F) settlement policy (ISSUE-294 Phase 1). Mirrors the
/// <see cref="StatutoryRule"/> effective-dating model: a settlement reads the policy version whose
/// <see cref="EffectiveFrom"/> is on or before the employee's last working day (latest wins). Editing the policy
/// creates a NEW effective-dated row; a settlement already computed keeps the version it used
/// (<c>FinalSettlement.PolicyEffectiveFrom</c>) so policy changes are never retroactive.
///
/// <para>When NO policy row exists for a tenant, the settlement engine falls back to a code-default (all three
/// includes true + <see cref="FinalPeriodOwnedBySettlement"/> true) — the feature functions without seeding.</para>
///
/// <para>Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + the dormant
/// Postgres RLS <c>tenant_isolation</c> policy shipped in its migration.</para>
/// </summary>
public sealed class TenantFnFPolicy : BaseEntity, IAuditExempt
{
    /// <summary>Effective-from date, inclusive (latest version on/before the LWD wins).</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Whether the settlement includes the pro-rated final-month base pay (default true).</summary>
    public bool IncludeProRatedFinalPay { get; set; } = true;

    /// <summary>Whether the settlement applies statutory deductions to the pro-rated final pay (default true).</summary>
    public bool IncludeStatutory { get; set; } = true;

    /// <summary>Whether the settlement includes forfeitable-leave encashment (default true).</summary>
    public bool IncludeLeaveEncashment { get; set; } = true;

    /// <summary>
    /// The double-pay boundary (default true). When true, the regular payroll run must NOT also pay an employee
    /// whose final period is owned by an F&amp;F settlement, so the final period has exactly one owner.
    /// </summary>
    public bool FinalPeriodOwnedBySettlement { get; set; } = true;

    /// <summary>Whether this policy version is active (default true).</summary>
    public bool IsActive { get; set; } = true;
}
