namespace HRM.Domain.Entities;

/// <summary>
/// A locked payroll period for a tenant: regularization (and, later, any retroactive attendance
/// edit) is blocked for dates that fall within an inclusive <see cref="StartDate"/>..<see cref="EndDate"/>
/// range (US-ATT-003 AC-5/FR-7/BR-6).
///
/// PLACEHOLDER (TODO US-PAY): the Payroll module does not exist yet, but AC-5 must be enforceable and
/// testable now. This is a minimal tenant-scoped representation of "which date ranges are locked" that
/// the regularization check consults. When the Payroll module lands it will OWN this concept (period
/// close/lock lifecycle, who locked it, etc.); this entity is the seam it plugs into. Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter. Maps to "payroll_lock_period".
/// </summary>
public sealed class PayrollLockPeriod : BaseEntity
{
    /// <summary>Inclusive first locked date.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Inclusive last locked date.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>
    /// Returns true when <paramref name="date"/> falls within the inclusive locked range.
    /// </summary>
    public bool Covers(DateOnly date) => date >= StartDate && date <= EndDate;
}
