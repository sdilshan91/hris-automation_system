using HRM.Application.Common.Interfaces;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="IHolidayProvider"/> that returns no holidays.
///
/// Retained for tests and as a fallback. Production registers the DB-backed
/// <see cref="HolidayProvider"/> (US-LV-007) instead.
/// </summary>
public sealed class NoOpHolidayProvider : IHolidayProvider
{
    private static readonly IReadOnlySet<DateOnly> Empty = new HashSet<DateOnly>();

    public Task<IReadOnlySet<DateOnly>> GetHolidaysAsync(
        DateOnly startInclusive,
        DateOnly endInclusive,
        Guid? locationId = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Empty);
}
