// ============================================================================
// CAL-6 / US-ATT-011 AC-5 (US-CHR-013): FteScaledOvertimeBase — the OT hourly base may optionally scale by the
// employee's FTE, so a part-timer's monthly basic buys proportionally fewer hours.
//
// hourly_rate = monthly_basic / (working_days * standard_hours [* fte when the flag is on]).
//
// The flag is OFF BY DEFAULT and that is the load-bearing property: with it off, `fte` is ignored entirely and
// the rate is byte-identical to its pre-US-CHR-013 value for every existing tenant. The `FlagOff_` arms below
// are the no-regression control, not a curiosity.
//
// Unit-level on purpose: PayrollOvertimeCalculator is a pure function (no DB/tenant/clock), the caller resolves
// the flag + FTE and passes them in. The wiring — that PayrollRunProcessor actually threads the resolved policy
// and the employee's FTE — is a separate concern; a green unit test here proves the MATH, not the plumbing.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Payroll;

namespace HRM.Tests.Unit;

public sealed class OvertimeFteBaseTests
{
    private const decimal MonthlyBasic = 22_000m;
    private const decimal WorkingDays = 22m;

    // 2 hours of OT at 2x, so the amount is a clean multiple of the hourly rate.
    private static readonly Dictionary<string, int> TwoHoursAtDouble = new() { ["2"] = 120 };

    // Full-time base: 22000 / (22 * 8) = 125.00/hour.
    private const decimal FullTimeHourly = 125.00m;

    // ══ Flag OFF — the DEFAULT and the no-regression contract ══

    /// <summary>
    /// TC-ATT-152 (AC-5, control): with the flag OFF — the default, and every existing tenant — a 0.5-FTE and a
    /// 1.0-FTE employee on the SAME monthly basic get the SAME OT hourly rate. FTE is ignored entirely.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-ATT-152")]
    [InlineData(1.00)]
    [InlineData(0.50)]
    [InlineData(0.20)]
    public void FlagOff_FteIsIgnored_HourlyRateIsUnchanged(double fte)
    {
        var result = PayrollOvertimeCalculator.Compute(
            MonthlyBasic, WorkingDays, TwoHoursAtDouble, totalApprovedMinutes: 120,
            fte: (decimal)fte, fteScaledBase: false);

        result.HourlyRate.Should().Be(
            FullTimeHourly, "the flag is OFF, so FTE must not touch the base — this is the no-regression contract");
        result.OvertimeAmount.Should().Be(500.00m, "2h * 125.00 * 2x");
    }

    /// <summary>
    /// Omitting BOTH new parameters (the pre-CAL-6 call shape) must behave exactly as before. Pins the
    /// trailing-optional defaults themselves, not just an explicit `false`.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-152")]
    public void DefaultCallShape_WithoutFteArguments_IsUnchanged()
    {
        var result = PayrollOvertimeCalculator.Compute(
            MonthlyBasic, WorkingDays, TwoHoursAtDouble, totalApprovedMinutes: 120);

        result.HourlyRate.Should().Be(FullTimeHourly);
        result.OvertimeAmount.Should().Be(500.00m);
    }

    // ══ Flag ON — the opt-in behaviour ══

    /// <summary>
    /// TC-ATT-152 (AC-5): with the flag ON, a 0.5-FTE employee's OT hourly rate is EXACTLY 2x a full-timer's on
    /// the same monthly basic — that basic buys half the hours, so each hour is worth double.
    /// 22000 / (22 * 8 * 0.5) = 250.00.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-152")]
    public void FlagOn_HalfFte_HourlyRateIsExactlyDouble()
    {
        var half = PayrollOvertimeCalculator.Compute(
            MonthlyBasic, WorkingDays, TwoHoursAtDouble, totalApprovedMinutes: 120,
            fte: 0.50m, fteScaledBase: true);

        var full = PayrollOvertimeCalculator.Compute(
            MonthlyBasic, WorkingDays, TwoHoursAtDouble, totalApprovedMinutes: 120,
            fte: 1.00m, fteScaledBase: true);

        half.HourlyRate.Should().Be(250.00m, "22000 / (22 * 8 * 0.5)");
        full.HourlyRate.Should().Be(FullTimeHourly, "a 1.0 FTE scales by 1 — unchanged even with the flag on");
        half.HourlyRate.Should().Be(full.HourlyRate * 2m, "exactly double, not merely greater");
        half.OvertimeAmount.Should().Be(1000.00m, "2h * 250.00 * 2x");
    }

    /// <summary>
    /// A 1.0-FTE employee is unaffected by the flag either way — turning it on must not disturb full-timers,
    /// who are the overwhelming majority. Distinguishes "scales by FTE" from "changes the base whenever on".
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-152")]
    public void FlagOn_FullTimeEmployee_IsIdenticalToFlagOff()
    {
        var on = PayrollOvertimeCalculator.Compute(
            MonthlyBasic, WorkingDays, TwoHoursAtDouble, totalApprovedMinutes: 120,
            fte: 1.00m, fteScaledBase: true);
        var off = PayrollOvertimeCalculator.Compute(
            MonthlyBasic, WorkingDays, TwoHoursAtDouble, totalApprovedMinutes: 120,
            fte: 1.00m, fteScaledBase: false);

        on.Should().Be(off);
    }

    /// <summary>
    /// A non-positive FTE must NOT be scaled: dividing by zero would throw and a negative would invert the rate
    /// into a negative payment. 0 is not a valid FTE (the create/update validators reject it), so an unscaled
    /// rate is the safe reading of a corrupt row — never an exception or negative pay on a money path.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-ATT-152")]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    public void FlagOn_NonPositiveFte_FallsBackToTheUnscaledBase_NeverThrowsOrGoesNegative(double fte)
    {
        var result = PayrollOvertimeCalculator.Compute(
            MonthlyBasic, WorkingDays, TwoHoursAtDouble, totalApprovedMinutes: 120,
            fte: (decimal)fte, fteScaledBase: true);

        result.HourlyRate.Should().Be(FullTimeHourly, "a corrupt FTE reads as unscaled, not as a divide-by-zero");
        result.OvertimeAmount.Should().BePositive();
    }
}
