namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Supplies absent working days for an employee, for the auto-LOP absenteeism job (US-LV-011 FR-2, AC-2).
///
/// SEAM: There is NO attendance module yet (US-ATT-* unbuilt). The default implementation
/// (<c>NoOpAttendanceProvider</c>) returns no absences, so <c>ProcessAbsenteeismJob</c> is wired,
/// idempotent, and tenant-safe but generates nothing until attendance lands. This mirrors the
/// <see cref="IHolidayProvider"/>/<c>NoOpHolidayProvider</c> seam pattern that US-LV-007 later
/// replaced with a real DB-backed provider.
///
/// TODO(US-ATT-*): replace the no-op with a real provider that reads attendance/clock-in records and
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
