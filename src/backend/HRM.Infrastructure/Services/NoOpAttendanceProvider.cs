using HRM.Application.Common.Interfaces;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="IAttendanceProvider"/> that returns no absences (US-LV-011 FR-2 seam).
///
/// There is no attendance module yet (US-ATT-* unbuilt), so the auto-LOP absenteeism job runs and is
/// idempotent/tenant-safe but generates nothing. When attendance lands, register a DB-backed provider
/// in DI (mirroring how US-LV-007 swapped NoOpHolidayProvider for the real HolidayProvider).
/// </summary>
public sealed class NoOpAttendanceProvider : IAttendanceProvider
{
    private static readonly IReadOnlySet<DateOnly> Empty = new HashSet<DateOnly>();

    public Task<IReadOnlySet<DateOnly>> GetAbsentWorkingDaysAsync(
        Guid employeeId,
        DateOnly startInclusive,
        DateOnly endInclusive,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Empty);
}
