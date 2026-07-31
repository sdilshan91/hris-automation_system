namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Supplies absent working days for an employee, for the auto-LOP absenteeism job (US-LV-011 FR-2, AC-2).
///
/// ⚠ ISSUE-357 — THE NOTE BELOW IS OUT OF DATE AND THE OBVIOUS FIX IS UNSAFE. Attendance HAS shipped
/// (US-ATT-001/002/008), so "no attendance module yet" is false. But payroll already deducts absence via
/// AttendanceMonthlySummary.LopDays in PayrollRunProcessor — swapping in a DB-backed provider here would
/// create a SECOND LOP ledger for the same days and double-deduct pay. Decide which rail is authoritative
/// first; the TODO below is not a green light.
///
/// SEAM (historical): There was no attendance module when this was written. The default implementation
/// (<c>NoOpAttendanceProvider</c>) returns no absences, so <c>ProcessAbsenteeismJob</c> is wired,
/// idempotent, and tenant-safe but generates nothing until attendance lands. This mirrors the
/// <see cref="IHolidayProvider"/>/<c>NoOpHolidayProvider</c> seam pattern that US-LV-007 later
/// replaced with a real DB-backed provider.
///
/// TODO(ISSUE-357 — BLOCKED on the authoritative-rail decision, NOT merely on attendance existing):
/// replace the no-op with a real provider that reads attendance/clock-in records and
/// returns working days with no clock-in AND no approved leave (an unaccounted absence).
/// </summary>
public interface IAttendanceProvider
{
    /// <summary>
    /// Returns the set of dates within the inclusive range on which the given employee was absent
    /// (a working day with no clock-in and no approved leave) for the current tenant. Working-day and
    /// holiday exclusion are the provider's responsibility — the job treats whatever is returned as an
    /// unaccounted absence requiring an LOP entry.
    /// </summary>
    Task<IReadOnlySet<DateOnly>> GetAbsentWorkingDaysAsync(
        Guid employeeId,
        DateOnly startInclusive,
        DateOnly endInclusive,
        CancellationToken cancellationToken = default);
}
