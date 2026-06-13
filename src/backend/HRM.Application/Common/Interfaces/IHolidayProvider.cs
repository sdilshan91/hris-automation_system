namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Supplies tenant-scoped public holidays for working-day calculations (US-LV-003 FR-3, AC-6).
///
/// SEAM: There is no holiday-calendar entity yet (US-LV-007 is unbuilt). The default
/// implementation (<c>NoOpHolidayProvider</c>) returns an empty set so the day-count
/// calculation excludes only weekends. When US-LV-007 ships, replace the registration with
/// a DB-backed implementation; no changes to the day-count logic will be required.
/// TODO(US-LV-007): Implement a holiday-calendar-backed provider.
/// </summary>
public interface IHolidayProvider
{
    /// <summary>
    /// Returns the set of public-holiday dates that fall within the given inclusive range
    /// for the current tenant. The default implementation returns an empty set.
    /// </summary>
    Task<IReadOnlySet<DateOnly>> GetHolidaysAsync(
        DateOnly startInclusive,
        DateOnly endInclusive,
        CancellationToken cancellationToken = default);
}
