// ============================================================================
// US-ATT-006: Pure overtime-calculation unit tests (AttendanceCalculator.CalculateOvertime +
// OvertimeMultiplierResolver). These are framework-free domain tests — deterministic and auditable
// (NFR-3). The clock-out wiring and persistence are covered by the integration tests.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Entities;

namespace HRM.Tests.Unit;

public sealed class OvertimeCalculatorTests
{
    // ── FR-1 / BR-1 / BR-2: auto-detect 9h on 8h shift with 30-min threshold => 60 min OT ──
    [Fact]
    public void CalculateOvertime_NineHoursOnEightHourShift_ReturnsExcess()
    {
        // 9h net work = 540; standard 480; excess 60 >= 30 threshold.
        var result = AttendanceCalculator.CalculateOvertime(
            netWorkMinutes: 540, standardMinutes: 480,
            minimumThresholdMinutes: 30, maxDailyMinutes: 240,
            multiplier: 1.5m, multiplierBasis: OvertimeMultiplierBasis.Weekday);

        result.OvertimeMinutes.Should().Be(60);
        result.CapApplied.Should().BeFalse();
        result.Basis.Should().Contain("rawExcess=60").And.Contain("overtime=60");
    }

    // Story test hint: exactly 8h30m on an 8h shift with 30-min threshold => 30 min OT (at threshold).
    [Fact]
    public void CalculateOvertime_ExactlyAtThreshold_IsOvertime()
    {
        var result = AttendanceCalculator.CalculateOvertime(
            netWorkMinutes: 510, standardMinutes: 480,
            minimumThresholdMinutes: 30, maxDailyMinutes: 240,
            multiplier: 1.5m, multiplierBasis: OvertimeMultiplierBasis.Weekday);

        result.OvertimeMinutes.Should().Be(30);
    }

    // ── BR-2: 8h20m on 8h shift with 30-min threshold => below threshold => NO overtime ──
    [Fact]
    public void CalculateOvertime_BelowThreshold_ReturnsZero()
    {
        // 8h20m = 500; standard 480; excess 20 < 30 threshold.
        var result = AttendanceCalculator.CalculateOvertime(
            netWorkMinutes: 500, standardMinutes: 480,
            minimumThresholdMinutes: 30, maxDailyMinutes: 240,
            multiplier: 1.5m, multiplierBasis: OvertimeMultiplierBasis.Weekday);

        result.OvertimeMinutes.Should().Be(0);
        result.Basis.Should().Contain("belowThreshold=true");
    }

    // ── BR-4: 14h on 8h shift with 4h daily cap => overtime capped at 240, flagged ──
    [Fact]
    public void CalculateOvertime_ExceedsDailyCap_IsCappedAndFlagged()
    {
        // 14h net = 840; standard 480; raw excess 360; cap 240.
        var result = AttendanceCalculator.CalculateOvertime(
            netWorkMinutes: 840, standardMinutes: 480,
            minimumThresholdMinutes: 30, maxDailyMinutes: 240,
            multiplier: 1.5m, multiplierBasis: OvertimeMultiplierBasis.Weekday);

        result.OvertimeMinutes.Should().Be(240);
        result.CapApplied.Should().BeTrue();
        result.Basis.Should().Contain("rawExcess=360").And.Contain("capApplied=True");
    }

    // ── BR-3 / BR-7: multiplier basis selection (holiday > weekend > weekday) ──
    [Fact]
    public void Resolve_Saturday_UsesWeekendMultiplier()
    {
        var saturday = new DateOnly(2026, 6, 13);   // a Saturday
        saturday.DayOfWeek.Should().Be(DayOfWeek.Saturday);

        var (multiplier, basis) = OvertimeMultiplierResolver.Resolve(
            saturday, isPublicHoliday: false,
            weekdayMultiplier: 1.5m, weekendMultiplier: 2.0m, holidayMultiplier: 2.5m);

        multiplier.Should().Be(2.0m);
        basis.Should().Be(OvertimeMultiplierBasis.Weekend);
    }

    [Fact]
    public void Resolve_PublicHoliday_UsesHolidayMultiplier_EvenOnWeekday()
    {
        var weekday = new DateOnly(2026, 6, 15);   // a Monday
        weekday.DayOfWeek.Should().Be(DayOfWeek.Monday);

        var (multiplier, basis) = OvertimeMultiplierResolver.Resolve(
            weekday, isPublicHoliday: true,
            weekdayMultiplier: 1.5m, weekendMultiplier: 2.0m, holidayMultiplier: 2.5m);

        multiplier.Should().Be(2.5m);
        basis.Should().Be(OvertimeMultiplierBasis.Holiday);
    }

    [Fact]
    public void Resolve_PlainWeekday_UsesWeekdayMultiplier()
    {
        var weekday = new DateOnly(2026, 6, 15);   // Monday

        var (multiplier, basis) = OvertimeMultiplierResolver.Resolve(
            weekday, isPublicHoliday: false,
            weekdayMultiplier: 1.5m, weekendMultiplier: 2.0m, holidayMultiplier: 2.5m);

        multiplier.Should().Be(1.5m);
        basis.Should().Be(OvertimeMultiplierBasis.Weekday);
    }
}
