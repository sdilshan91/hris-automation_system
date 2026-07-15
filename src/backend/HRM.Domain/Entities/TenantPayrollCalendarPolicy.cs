namespace HRM.Domain.Entities;

/// <summary>
/// A per-tenant, effective-dated payroll CALENDAR policy (US-ATT-011 AC-4 / FR-5, CAL-5). Mirrors the
/// <see cref="TenantFnFPolicy"/> effective-dating model: a payroll run reads the policy version whose
/// <see cref="EffectiveFrom"/> is on or before the pay-period date (latest wins). Editing the policy creates a
/// NEW effective-dated row, so a change applies to the NEXT period and is never retroactive to a period whose
/// figures are already computed.
///
/// <para>Governs whether a public holiday counts as a working day for payroll. This is money-critical: the
/// working-days figure is the denominator of the pro-ration factor, the LOP daily rate
/// (<c>basic / working_days</c>) and the overtime hourly rate (<c>basic / (working_days * 8)</c>) — so
/// excluding holidays RAISES the OT hourly base and the LOP daily cost.</para>
///
/// <para><b>Why the default is FALSE (deliberate, not a preference):</b> defaulting to true would silently
/// change the OT base and LOP rate for EVERY existing tenant on their next payroll run. Per the F&amp;F
/// "never retroactive" precedent, a money-affecting change must be opted into on an effective date the tenant
/// chooses. With no policy row the engine uses the code-default FALSE and every figure is byte-identical to
/// the pre-CAL-5 behaviour.</para>
///
/// <para>Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + the dormant
/// Postgres RLS <c>tenant_isolation</c> policy shipped in its migration.</para>
/// </summary>
public sealed class TenantPayrollCalendarPolicy : BaseEntity, IAuditExempt
{
    /// <summary>Effective-from date, inclusive (latest version on/before the pay-period date wins).</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>
    /// Whether a public holiday is excluded from the payroll working-days count — i.e. a holiday is simply NOT
    /// a working day (default FALSE; see the type remarks for why). When true, the exclusion is applied
    /// CONSISTENTLY to both the pro-ration numerator and denominator, on the employee's LOCATION-scoped holiday
    /// set. When false, no holiday set is threaded anywhere and the engine behaves exactly as it did pre-CAL-5.
    /// </summary>
    public bool ExcludeHolidaysFromWorkingDays { get; set; } = false;

    /// <summary>Whether this policy version is active (default true).</summary>
    public bool IsActive { get; set; } = true;
}
