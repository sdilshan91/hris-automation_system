// ============================================================================
// BUG-078 (+ OL-1): The OT hourly-rate base — and Basic-based statutory EPF/ETF —
// must derive from the employee's BASIC line, identified by component CODE ("BASIC"),
// NOT by the display Name. Real structures name the line "Basic Salary", so the old
// Name-match fell through to GrossEarnings, over-basing OT ~2.5x and over-deducting
// Basic-based statutory. Both paths now share PayrollRunProcessor.ResolvedBasic.
//
// Reproduces the finding's live evidence (June-2026 run, EMP-0001):
//   Basic Salary 10663.64 + Conveyance 1066.36 + HRA 2132.73 + Special 12796.36 = 26659.09 gross.
//   Correct OT hourly = 10663.64 / (22*8) = 60.59/h  (NOT 26659.09/176 = 151.47/h).
// ============================================================================

using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class PayrollBasicResolutionTests
{
    private static readonly Guid BasicId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid ConveyanceId = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
    private static readonly Guid HraId = Guid.Parse("00000000-0000-0000-0000-0000000000d3");
    private static readonly Guid SpecialId = Guid.Parse("00000000-0000-0000-0000-0000000000e4");

    private const decimal Basic = 10663.64m;
    private const decimal Conveyance = 1066.36m;
    private const decimal Hra = 2132.73m;
    private const decimal Special = 12796.36m;
    private const decimal Gross = Basic + Conveyance + Hra + Special; // 26659.09

    // The multi-earning structure that surfaced BUG-078: the basic line is named "Basic Salary",
    // so a display-Name match on "Basic" would MISS it and fall through to gross.
    private static IReadOnlyList<PayrollComponentInput> Inputs() => new[]
    {
        new PayrollComponentInput(BasicId, "BASIC", "Basic Salary", SalaryComponentType.Earning, false, Basic, 1),
        new PayrollComponentInput(ConveyanceId, "CONVEYANCE", "Conveyance", SalaryComponentType.Earning, false, Conveyance, 2),
        new PayrollComponentInput(HraId, "HRA", "HRA", SalaryComponentType.Earning, false, Hra, 3),
        new PayrollComponentInput(SpecialId, "SPECIAL", "Special Allowance", SalaryComponentType.Earning, false, Special, 4),
    };

    private static PayrollSlipResult SlipWith(IReadOnlyList<PayrollSlipLine> lines) =>
        new(Guid.NewGuid(), Gross, 0m, Gross, 0m, WorkingDays: 22m, PaidDays: 22m, LopDays: 0m, lines);

    private static IReadOnlyList<PayrollSlipLine> Lines() => new[]
    {
        new PayrollSlipLine(BasicId, "Basic Salary", SalaryComponentType.Earning, false, Basic, "full month"),
        new PayrollSlipLine(ConveyanceId, "Conveyance", SalaryComponentType.Earning, false, Conveyance, "full month"),
        new PayrollSlipLine(HraId, "HRA", SalaryComponentType.Earning, false, Hra, "full month"),
        new PayrollSlipLine(SpecialId, "Special Allowance", SalaryComponentType.Earning, false, Special, "full month"),
    };

    [Fact]
    public void ResolvedBasic_MultiComponentStructure_ReturnsBasicLine_NotGross_BUG078()
    {
        // KEY REGRESSION: pre-fix (Name-match on "Basic") this returned Gross (26659.09) because the line is
        // named "Basic Salary". Post-fix (Code-match "BASIC" -> ComponentId) it returns the BASIC line amount.
        var basic = PayrollRunProcessor.ResolvedBasic(SlipWith(Lines()), Inputs());

        basic.Should().Be(Basic, "BASIC is identified by Code, so a line named 'Basic Salary' still resolves");
        basic.Should().NotBe(Gross, "the OT/statutory base must be BASIC, never total earnings");
    }

    [Fact]
    public void OvertimeHourlyRate_UsesBasic_NotGross_BUG078()
    {
        var basic = PayrollRunProcessor.ResolvedBasic(SlipWith(Lines()), Inputs());

        // 6h approved OT at 1.5x over a 22-working-day month.
        var ot = PayrollOvertimeCalculator.Compute(basic, workingDays: 22m,
            multiplierBuckets: new Dictionary<string, int> { ["1.5"] = 360 }, totalApprovedMinutes: 360);

        ot.HourlyRate.Should().BeApproximately(60.59m, 0.01m, "10663.64 / (22*8) — basic-based, not gross-based");
        ot.HourlyRate.Should().NotBeApproximately(151.47m, 0.5m, "151.47 = 26659.09/176 was the ~2.5x-overpaid gross-based rate");
        ot.OvertimeAmount.Should().BeApproximately(545.31m, 0.5m, "6h * 60.59 * 1.5 — not the buggy 1363.25");
    }

    [Fact]
    public void ResolvedBasic_NoBasicComponent_FallsBackToGross()
    {
        // NEGATIVE ARM: a structure with no BASIC code legitimately falls back to gross (unchanged behavior).
        var inputsNoBasic = Inputs().Where(i => i.Code != "BASIC").ToArray();
        var linesNoBasic = Lines().Where(l => l.ComponentId != BasicId).ToArray();
        var slip = new PayrollSlipResult(Guid.NewGuid(), Conveyance + Hra + Special, 0m, Conveyance + Hra + Special,
            0m, 22m, 22m, 0m, linesNoBasic);

        var basic = PayrollRunProcessor.ResolvedBasic(slip, inputsNoBasic);

        basic.Should().Be(Conveyance + Hra + Special, "no identifiable BASIC line -> gross fallback");
    }
}
