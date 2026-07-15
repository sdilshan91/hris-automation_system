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

    // ── BUG-285 / US-ATT-011 AC-2: the weekend basis comes from the RESOLVED work-week ──
    //
    // The three arms above deliberately omit `workingDays` and therefore pin the LEGACY Sat/Sun fallback —
    // they are the backward-compat control for a caller that cannot resolve a week. The arms below prove the
    // real behaviour: "weekend" means "not a working day for THIS employee".

    /// <summary>
    /// TC-ATT-153 step 1 (BUG-285): a Gulf employee works Sun–Thu, so FRIDAY is their weekend and OT on it
    /// must pay the WEEKEND multiplier. Pre-fix the hardcoded `DayOfWeek is Saturday or Sunday` check called
    /// Friday a weekday and underpaid it at 1.5× — straight into payroll earnings.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-153")]
    public void Resolve_GulfSunThu_FridayIsWeekend_UsesWeekendMultiplier()
    {
        var friday = new DateOnly(2026, 6, 12);
        friday.DayOfWeek.Should().Be(DayOfWeek.Friday);

        var (multiplier, basis) = OvertimeMultiplierResolver.Resolve(
            friday, isPublicHoliday: false,
            weekdayMultiplier: 1.5m, weekendMultiplier: 2.0m, holidayMultiplier: 2.5m,
            workingDays: new HashSet<int> { 7, 1, 2, 3, 4 });   // Sun-Thu

        multiplier.Should().Be(2.0m, "Friday is the Gulf weekend — the hardcoded Sat/Sun check paid 1.5×");
        basis.Should().Be(OvertimeMultiplierBasis.Weekend);
    }

    /// <summary>
    /// TC-ATT-153 step 2 (BUG-285): the mirror — SUNDAY is a Gulf workday, so OT on it pays the WEEKDAY
    /// multiplier. Pre-fix the hardcode called Sunday a weekend and OVERPAID it at 2.0×.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-153")]
    public void Resolve_GulfSunThu_SundayIsWorkday_UsesWeekdayMultiplier()
    {
        var sunday = new DateOnly(2026, 6, 14);
        sunday.DayOfWeek.Should().Be(DayOfWeek.Sunday);

        var (multiplier, basis) = OvertimeMultiplierResolver.Resolve(
            sunday, isPublicHoliday: false,
            weekdayMultiplier: 1.5m, weekendMultiplier: 2.0m, holidayMultiplier: 2.5m,
            workingDays: new HashSet<int> { 7, 1, 2, 3, 4 });   // Sun-Thu

        multiplier.Should().Be(1.5m, "Sunday IS a Gulf workday — the hardcoded Sat/Sun check paid 2.0×");
        basis.Should().Be(OvertimeMultiplierBasis.Weekday);
    }

    /// <summary>
    /// Pins the ISO→date bridge at BOTH risky ends via a six-day Mon–Sat week (mainstream in LK/Gulf retail
    /// and construction): SATURDAY (ISO 6) is a workday → weekday multiplier; SUNDAY (ISO 7) is not → weekend.
    /// Saturday is the value a bridge off-by-one corrupts, and no other arm here would notice.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-ATT-153")]
    [InlineData("2026-06-13", 1.5, OvertimeMultiplierBasis.Weekday)]   // Saturday — ISO 6, a workday
    [InlineData("2026-06-14", 2.0, OvertimeMultiplierBasis.Weekend)]   // Sunday   — ISO 7, their weekend
    public void Resolve_MonSatSixDayWeek_SaturdayIsWorkday_SundayIsWeekend(
        string isoDate, double expectedMultiplier, string expectedBasis)
    {
        var date = DateOnly.Parse(isoDate);

        var (multiplier, basis) = OvertimeMultiplierResolver.Resolve(
            date, isPublicHoliday: false,
            weekdayMultiplier: 1.5m, weekendMultiplier: 2.0m, holidayMultiplier: 2.5m,
            workingDays: new HashSet<int> { 1, 2, 3, 4, 5, 6 });   // Mon-Sat

        multiplier.Should().Be((decimal)expectedMultiplier);
        basis.Should().Be(expectedBasis);
    }

    /// <summary>
    /// BR-3 precedence is unchanged by the work-week: a public holiday still outranks the weekend/weekday
    /// decision, even on a day that IS a working day for this employee.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-153")]
    public void Resolve_HolidayStillOutranksTheResolvedWorkWeek()
    {
        var sunday = new DateOnly(2026, 6, 14);   // a Gulf WORKDAY

        var (multiplier, basis) = OvertimeMultiplierResolver.Resolve(
            sunday, isPublicHoliday: true,
            weekdayMultiplier: 1.5m, weekendMultiplier: 2.0m, holidayMultiplier: 2.5m,
            workingDays: new HashSet<int> { 7, 1, 2, 3, 4 });

        multiplier.Should().Be(2.5m);
        basis.Should().Be(OvertimeMultiplierBasis.Holiday);
    }

    /// <summary>
    /// An EMPTY working-day set must NOT be read as "every day is a weekend" (which would pay every OT hour at
    /// the weekend rate). It falls back to the legacy Sat/Sun basis — a defensive backstop only:
    /// ShiftScheduleResolver never returns an empty set, so production always passes a real week.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-ATT-153")]
    [InlineData("2026-06-13", 2.0, OvertimeMultiplierBasis.Weekend)]   // Saturday
    [InlineData("2026-06-15", 1.5, OvertimeMultiplierBasis.Weekday)]   // Monday
    public void Resolve_EmptyWorkingDaySet_FallsBackToLegacySatSun_NotAllWeekend(
        string isoDate, double expectedMultiplier, string expectedBasis)
    {
        var date = DateOnly.Parse(isoDate);

        var (multiplier, basis) = OvertimeMultiplierResolver.Resolve(
            date, isPublicHoliday: false,
            weekdayMultiplier: 1.5m, weekendMultiplier: 2.0m, holidayMultiplier: 2.5m,
            workingDays: new HashSet<int>());

        multiplier.Should().Be((decimal)expectedMultiplier);
        basis.Should().Be(expectedBasis);
    }
}
