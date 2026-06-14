// ============================================================================
// US-ATT-002: Pure work-hours calculator unit tests (AttendanceCalculator).
// Validates auto-break (FR-3/BR-2), overtime (FR-4/BR-3), short-day (BR-4),
// anomaly >16h (FR-7/BR-6), system-close anomaly (BR-5), minute accuracy (NFR-2),
// and status precedence — independent of EF/DB.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Entities;

namespace HRM.Tests.Unit;

public sealed class AttendanceCalculatorTests
{
    private static AttendanceSettings Settings(
        int standard = 480, int minimum = 240, int breakMin = 60, int breakThreshold = 360, int otThreshold = 0)
        => new()
        {
            StandardWorkMinutes = standard,
            MinimumWorkMinutes = minimum,
            AutoBreakMinutes = breakMin,
            AutoBreakThresholdMinutes = breakThreshold,
            OvertimeThresholdMinutes = otThreshold,
        };

    private static AttendanceCalculation Calc(int spanMinutes, AttendanceSettings? settings = null, bool system = false)
    {
        var clockIn = new DateTime(2026, 6, 14, 8, 0, 0, DateTimeKind.Utc);
        return AttendanceCalculator.Calculate(
            clockIn, clockIn.AddMinutes(spanMinutes), settings ?? Settings(), system);
    }

    [Fact]
    public void EightHourDay_DeductsBreak_IsComplete()
    {
        var r = Calc(480);
        r.TotalWorkMinutes.Should().Be(420);   // 480 - 60 break
        r.OvertimeMinutes.Should().Be(0);
        r.Status.Should().Be("COMPLETE");
    }

    [Fact]
    public void BelowBreakThreshold_NoDeduction()
    {
        var r = Calc(300);                      // 5h <= 6h threshold
        r.TotalWorkMinutes.Should().Be(300);
    }

    [Fact]
    public void TenHours_NoBreak_HasOneHundredTwentyOvertime()
    {
        var r = Calc(600, Settings(breakMin: 0));
        r.TotalWorkMinutes.Should().Be(600);
        r.OvertimeMinutes.Should().Be(120);
        r.Status.Should().Be("OVERTIME");
    }

    [Fact]
    public void OvertimeThreshold_AbsorbsSmallExcess()
    {
        // 540 net (no break), standard 480, threshold 60 => no overtime.
        var r = Calc(540, Settings(breakMin: 0, otThreshold: 60));
        r.OvertimeMinutes.Should().Be(0);
        r.Status.Should().Be("COMPLETE");
    }

    [Fact]
    public void ShortDay_BelowMinimum()
    {
        var r = Calc(180);                      // 3h < 4h minimum
        r.TotalWorkMinutes.Should().Be(180);
        r.Status.Should().Be("SHORT_DAY");
    }

    [Fact]
    public void Anomaly_WhenSpanExceeds16Hours()
    {
        var r = Calc(17 * 60);
        r.Status.Should().Be("ANOMALY");
    }

    [Fact]
    public void Anomaly_WhenSystemClosed_RegardlessOfSpan()
    {
        var r = Calc(480, system: true);
        r.Status.Should().Be("ANOMALY");
    }

    [Fact]
    public void MinuteAccurate_TruncatesPartialMinute()
    {
        var clockIn = new DateTime(2026, 6, 14, 8, 0, 0, DateTimeKind.Utc);
        var clockOut = clockIn.AddMinutes(300).AddSeconds(59);
        var r = AttendanceCalculator.Calculate(clockIn, clockOut, Settings());
        r.TotalWorkMinutes.Should().Be(300);    // 59s does not round up
    }

    [Fact]
    public void NeverNegative_WhenClockOutBeforeClockIn()
    {
        var clockIn = new DateTime(2026, 6, 14, 8, 0, 0, DateTimeKind.Utc);
        var r = AttendanceCalculator.Calculate(clockIn, clockIn.AddMinutes(-10), Settings());
        r.TotalWorkMinutes.Should().Be(0);
    }
}
