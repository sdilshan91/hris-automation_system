// ============================================================================
// US-ATT-008: Pure late-arrival / early-departure detection unit tests
// (LateEarlyCalculator). Framework-free domain tests — the single source of truth the inline
// clock-in/out path, the regularization-approval recompute (BR-7) and the monthly summary all share.
// Covers: late with grace (BR-1 / AC-1 late_minutes vs late_by), within-grace not-late, on-time,
// early departure with the minimum-hours gate (BR-2 / AC-3), grace-not-applied-to-early (§10), and
// the FLEXIBLE / no-shift exemption (BR-6 / §10 — modeled as null start/end).
// ============================================================================

using FluentAssertions;
using HRM.Domain.Entities;

namespace HRM.Tests.Unit;

public sealed class LateEarlyCalculatorTests
{
    private static readonly TimeOnly NineAm = new(9, 0);
    private static readonly TimeOnly FivePm = new(17, 0);

    // BR-1 / AC-1: 09:20 vs 09:00 start, 15 grace => late; late_minutes=20 (past START), late_by=5
    [Fact]
    public void Evaluate_PastStartBeyondGrace_IsLate_WithMinutesPastStartAndPastGrace()
    {
        var result = LateEarlyCalculator.Evaluate(
            clockInTime: new TimeOnly(9, 20),
            clockOutTime: FivePm,
            shiftStart: NineAm,
            shiftEnd: FivePm,
            gracePeriodMinutes: 15,
            workedMinutes: 460,
            minimumMinutes: 0);

        result.IsLate.Should().BeTrue();
        result.LateMinutes.Should().Be(20);   // AC-1: minutes past the shift START
        result.LateByMinutes.Should().Be(5);  // AC-1: minutes past the GRACE threshold
        result.IsEarlyDeparture.Should().BeFalse();
    }

    // BR-1: clock-in within grace is NOT late (09:10 vs 09:00 + 15 grace)
    [Fact]
    public void Evaluate_WithinGrace_IsNotLate()
    {
        var result = LateEarlyCalculator.Evaluate(
            clockInTime: new TimeOnly(9, 10),
            clockOutTime: FivePm,
            shiftStart: NineAm,
            shiftEnd: FivePm,
            gracePeriodMinutes: 15,
            workedMinutes: 470,
            minimumMinutes: 0);

        result.IsLate.Should().BeFalse();
        result.LateMinutes.Should().Be(10);   // still recorded as past-start, but not flagged late
        result.LateByMinutes.Should().Be(0);
    }

    // BR-1: exactly at start + grace boundary is NOT late (09:15 vs 09:00 + 15)
    [Fact]
    public void Evaluate_ExactlyAtGraceBoundary_IsNotLate()
    {
        var result = LateEarlyCalculator.Evaluate(
            clockInTime: new TimeOnly(9, 15),
            clockOutTime: FivePm,
            shiftStart: NineAm, shiftEnd: FivePm,
            gracePeriodMinutes: 15, workedMinutes: 465, minimumMinutes: 0);

        result.IsLate.Should().BeFalse();
        result.LateByMinutes.Should().Be(0);
    }

    // BR-2 / AC-3: clock-out before shift end with minimum unmet => early; minutes before END
    [Fact]
    public void Evaluate_BeforeEndAndMinimumUnmet_IsEarlyDeparture()
    {
        var result = LateEarlyCalculator.Evaluate(
            clockInTime: NineAm,
            clockOutTime: new TimeOnly(16, 30),   // 30 min before 17:00
            shiftStart: NineAm, shiftEnd: FivePm,
            gracePeriodMinutes: 15,
            workedMinutes: 450,                    // below 480 minimum
            minimumMinutes: 480);

        result.IsEarlyDeparture.Should().BeTrue();
        result.EarlyDepartureMinutes.Should().Be(30);   // AC-3: minutes before the shift END
    }

    // BR-2: clock-out before end BUT minimum hours met => NOT early
    [Fact]
    public void Evaluate_BeforeEndButMinimumMet_IsNotEarly()
    {
        var result = LateEarlyCalculator.Evaluate(
            clockInTime: NineAm,
            clockOutTime: new TimeOnly(16, 30),
            shiftStart: NineAm, shiftEnd: FivePm,
            gracePeriodMinutes: 15,
            workedMinutes: 480,         // meets the 480 minimum
            minimumMinutes: 480);

        result.IsEarlyDeparture.Should().BeFalse();
        result.EarlyDepartureMinutes.Should().Be(0);
    }

    // BR-2 / §10: grace does NOT apply to early departure
    [Fact]
    public void Evaluate_GraceNotAppliedToEarlyDeparture()
    {
        var result = LateEarlyCalculator.Evaluate(
            clockInTime: NineAm,
            clockOutTime: new TimeOnly(16, 55),    // only 5 min before end
            shiftStart: NineAm, shiftEnd: FivePm,
            gracePeriodMinutes: 15,                // 15-min grace must NOT swallow the 5-min early
            workedMinutes: 0, minimumMinutes: 0);

        result.IsEarlyDeparture.Should().BeTrue();
        result.EarlyDepartureMinutes.Should().Be(5);
    }

    // BR-2: when no minimum is configured (0), any pre-end clock-out is early
    [Fact]
    public void Evaluate_NoMinimumConfigured_PreEndIsEarly()
    {
        var result = LateEarlyCalculator.Evaluate(
            clockInTime: NineAm,
            clockOutTime: new TimeOnly(15, 30),
            shiftStart: NineAm, shiftEnd: FivePm,
            gracePeriodMinutes: 15, workedMinutes: 360, minimumMinutes: 0);

        result.IsEarlyDeparture.Should().BeTrue();
        result.EarlyDepartureMinutes.Should().Be(90);
    }

    // BR-6 / §10: FLEXIBLE / no fixed shift start => never late (modeled as null start)
    [Fact]
    public void Evaluate_NoShiftStart_IsNeverLate()
    {
        var result = LateEarlyCalculator.Evaluate(
            clockInTime: new TimeOnly(11, 0),
            clockOutTime: FivePm,
            shiftStart: null, shiftEnd: FivePm,
            gracePeriodMinutes: 15, workedMinutes: 360, minimumMinutes: 0);

        result.IsLate.Should().BeFalse();
        result.LateMinutes.Should().Be(0);
    }

    // BR-6 / §10: FLEXIBLE / no fixed shift end => never early (modeled as null end)
    [Fact]
    public void Evaluate_NoShiftEnd_IsNeverEarly()
    {
        var result = LateEarlyCalculator.Evaluate(
            clockInTime: NineAm,
            clockOutTime: new TimeOnly(12, 0),
            shiftStart: NineAm, shiftEnd: null,
            gracePeriodMinutes: 15, workedMinutes: 180, minimumMinutes: 0);

        result.IsEarlyDeparture.Should().BeFalse();
        result.EarlyDepartureMinutes.Should().Be(0);
    }

    // Open punch (no clock-out yet): early departure cannot be evaluated
    [Fact]
    public void Evaluate_OpenPunch_NoEarlyDeparture()
    {
        var result = LateEarlyCalculator.Evaluate(
            clockInTime: new TimeOnly(9, 30),
            clockOutTime: null,
            shiftStart: NineAm, shiftEnd: FivePm,
            gracePeriodMinutes: 15, workedMinutes: null, minimumMinutes: 480);

        result.IsLate.Should().BeTrue();        // late is computable on clock-in
        result.IsEarlyDeparture.Should().BeFalse();
        result.EarlyDepartureMinutes.Should().Be(0);
    }
}
