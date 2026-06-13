// ============================================================================
// US-LV-003: Working-days calculator unit tests (FR-3, AC-6).
// Pure-logic: weekend exclusion, holiday exclusion, configurable work-week,
// inverted ranges, single-day ranges.
// ============================================================================

using FluentAssertions;
using HRM.Application.Features.LeaveRequests;

namespace HRM.Tests.Unit;

public sealed class WorkingDaysCalculatorTests
{
    [Fact]
    public void FullWorkWeek_MonToFri_CountsFive()
    {
        // Mon 2026-06-15 .. Fri 2026-06-19
        var result = WorkingDaysCalculator.CountWorkingDays(
            new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 19));

        result.Should().Be(5m);
    }

    [Fact]
    public void RangeSpanningWeekend_ExcludesSatAndSun()
    {
        // Fri 2026-06-19 .. Mon 2026-06-22 => Fri + Mon = 2 working days.
        var result = WorkingDaysCalculator.CountWorkingDays(
            new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 22));

        result.Should().Be(2m);
    }

    [Fact]
    public void Holiday_OnWeekday_IsExcluded()
    {
        // Mon-Fri with Wednesday (2026-06-17) a holiday => 4 days (test hint).
        var holidays = new HashSet<DateOnly> { new(2026, 6, 17) };

        var result = WorkingDaysCalculator.CountWorkingDays(
            new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 19), holidays: holidays);

        result.Should().Be(4m);
    }

    [Fact]
    public void Holiday_OnWeekend_DoesNotDoubleCount()
    {
        // Saturday holiday inside a Mon-Fri range has no effect.
        var holidays = new HashSet<DateOnly> { new(2026, 6, 20) };

        var result = WorkingDaysCalculator.CountWorkingDays(
            new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 19), holidays: holidays);

        result.Should().Be(5m);
    }

    [Fact]
    public void SixDayWorkWeek_IncludesSaturday()
    {
        var sixDay = new HashSet<DayOfWeek>
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday,
        };

        // Mon .. Sat = 6 working days when Saturday is a working day.
        var result = WorkingDaysCalculator.CountWorkingDays(
            new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 20), workWeek: sixDay);

        result.Should().Be(6m);
    }

    [Fact]
    public void InvertedRange_ReturnsZero()
    {
        var result = WorkingDaysCalculator.CountWorkingDays(
            new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 15));

        result.Should().Be(0m);
    }

    [Fact]
    public void SingleWeekday_ReturnsOne()
    {
        var result = WorkingDaysCalculator.CountWorkingDays(
            new DateOnly(2026, 6, 16), new DateOnly(2026, 6, 16));

        result.Should().Be(1m);
    }

    [Fact]
    public void SingleWeekendDay_ReturnsZero()
    {
        // Sunday 2026-06-21
        var result = WorkingDaysCalculator.CountWorkingDays(
            new DateOnly(2026, 6, 21), new DateOnly(2026, 6, 21));

        result.Should().Be(0m);
    }
}
