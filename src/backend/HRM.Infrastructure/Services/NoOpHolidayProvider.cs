using HRM.Application.Common.Interfaces;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="IHolidayProvider"/> that returns no holidays (US-LV-003 seam).
///
/// There is no holiday-calendar entity yet (US-LV-007 is unbuilt), so the working-day
/// calculation excludes weekends only. When US-LV-007 ships, register a DB-backed provider
/// in its place — the day-count logic already consumes whatever this returns.
/// TODO(US-LV-007): Replace with a holiday-calendar-backed implementation.
/// </summary>
public sealed class NoOpHolidayProvider : IHolidayProvider
{
    private static readonly IReadOnlySet<DateOnly> Empty = new HashSet<DateOnly>();

    public Task<IReadOnlySet<DateOnly>> GetHolidaysAsync(
        DateOnly startInclusive,
        DateOnly endInclusive,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Empty);
}
