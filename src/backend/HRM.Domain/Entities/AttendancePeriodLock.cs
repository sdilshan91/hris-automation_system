namespace HRM.Domain.Entities;

/// <summary>
/// Canonical attendance period lock (US-ATT-009 §7, FR-3/FR-4, AC-4/AC-5). When a row is
/// <see cref="IsLocked"/> = true for an inclusive <see cref="PeriodStart"/>..<see cref="PeriodEnd"/>
/// date range, all retroactive attendance changes for dates in that range are frozen:
/// clock-in, clock-out, regularization submit, and regularization approval are rejected with the
/// exact message "This date falls within a locked payroll period. Please contact HR." (BR-1).
///
/// This entity CONSOLIDATES the former US-ATT-003 placeholder <c>PayrollLockPeriod</c> into one
/// lock concept (there is no longer a competing lock table). HR locks the period before a payroll
/// run (BR-1) and may unlock it to correct an error, then re-lock (AC-5). Lock/unlock are audited
/// in-row via the <see cref="LockedBy"/>/<see cref="LockedAt"/>/<see cref="UnlockedBy"/>/
/// <see cref="UnlockedAt"/> fields AND the platform <c>AuditInterceptor</c> (FR-4).
///
/// The Payroll module (module 6) does not exist yet; this is the attendance-side lock it will
/// consult before finalizing a run. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF
/// global query filter (no PostgreSQL RLS — same deferral as the rest of the module).
/// Maps to the "attendance_period_lock" table.
/// </summary>
public sealed class AttendancePeriodLock : BaseEntity, IAuditExempt
{
    /// <summary>Inclusive first locked date.</summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>Inclusive last locked date.</summary>
    public DateOnly PeriodEnd { get; set; }

    /// <summary>
    /// True while the period is locked. An unlocked row (false) is a historical record that no
    /// longer freezes its dates — the lock checks only treat <c>IsLocked == true</c> rows as active.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>The user (HR Officer) who locked the period (FR-4).</summary>
    public Guid? LockedBy { get; set; }

    /// <summary>When the period was locked (UTC, FR-4).</summary>
    public DateTime? LockedAt { get; set; }

    /// <summary>The user (HR Officer) who unlocked the period (FR-4); null while still locked.</summary>
    public Guid? UnlockedBy { get; set; }

    /// <summary>When the period was unlocked (UTC, FR-4); null while still locked.</summary>
    public DateTime? UnlockedAt { get; set; }

    /// <summary>
    /// Returns true when <paramref name="date"/> falls within the inclusive locked range AND the
    /// lock is currently active.
    /// </summary>
    public bool Covers(DateOnly date) => IsLocked && date >= PeriodStart && date <= PeriodEnd;
}
