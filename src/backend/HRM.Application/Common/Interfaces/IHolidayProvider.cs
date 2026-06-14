namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Supplies tenant-scoped public holidays for working-day calculations (US-LV-003 FR-3, AC-6).
///
/// US-LV-007 made this real: the DB-backed <c>HolidayProvider</c> returns active PUBLIC holidays
/// from the holiday calendar for the current tenant, optionally narrowed to an employee's location.
/// The day-count logic (<c>WorkingDaysCalculator</c>) excludes whatever this returns.
/// </summary>
public interface IHolidayProvider
{
    /// <summary>
    /// Returns the set of public-holiday dates that fall within the given inclusive range
    /// for the current tenant. When <paramref name="locationId"/> is supplied, the result
    /// includes tenant-wide holidays (no location) plus holidays scoped to that location;
    /// when null, only tenant-wide holidays are returned (US-LV-007 BR-2, location filtering).
    /// </summary>
    Task<IReadOnlySet<DateOnly>> GetHolidaysAsync(
        DateOnly startInclusive,
        DateOnly endInclusive,
        Guid? locationId = null,
        CancellationToken cancellationToken = default);
}
