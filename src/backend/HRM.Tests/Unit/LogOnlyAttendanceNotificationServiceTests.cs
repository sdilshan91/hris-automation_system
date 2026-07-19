// ============================================================================
// US-ATT-008 FR-7 (DF-33 / ISSUE-087) — LogOnlyAttendanceNotificationService is the test/seam
// sibling of RealAttendanceNotificationService. This guards that the new chronic-lateness method is a
// log-only no-op that never throws (the notification legs must never break the committed attendance write).
// ============================================================================

using FluentAssertions;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Unit;

public sealed class LogOnlyAttendanceNotificationServiceTests
{
    private static LogOnlyAttendanceNotificationService Service() =>
        new(NullLogger<LogOnlyAttendanceNotificationService>.Instance);

    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public async Task NotifyChronicLatenessAsync_IsANoOp_ThatDoesNotThrow()
    {
        var act = () => Service().NotifyChronicLatenessAsync(
            Guid.NewGuid(), lateCount: 6, threshold: 5, new DateOnly(2026, 7, 15));

        await act.Should().NotThrowAsync();
    }
}
