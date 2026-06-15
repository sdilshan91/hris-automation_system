// ============================================================================
// US-PAY-002: CtcBreakdownCalculator unit tests.
// Pure-logic coverage of the CTC -> component resolution (FR-2/FR-3): Fixed annual amounts,
// PercentageOfGross (% of CTC), PercentageOfBasic (% of resolved Basic), Formula via SalaryFormula,
// per-component overrides (AC-3), monthly = annual/12, and the earnings total used for FR-6.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;

namespace HRM.Tests.Unit;

public sealed class CtcBreakdownCalculatorTests
{
    private static CtcComponentRule Rule(
        string code, SalaryComponentType type, CalculationMethod method,
        decimal? value, int order, string? formula = null)
        => new(Guid.NewGuid(), code, type, method, value, formula, order);

    // Test hint: Basic 40% of CTC, HRA 20% of Basic, fixed Conveyance.
    [Fact]
    public void Calculate_Basic40_Hra20OfBasic_FixedConveyance_ResolvesExpectedAmounts()
    {
        var basic = Rule("BASIC", SalaryComponentType.Earning, CalculationMethod.PercentageOfGross, 40m, 1);
        var hra = Rule("HRA", SalaryComponentType.Earning, CalculationMethod.PercentageOfBasic, 20m, 2);
        var conveyance = Rule("CONV", SalaryComponentType.Earning, CalculationMethod.Fixed, 24000m, 3);

        var result = CtcBreakdownCalculator.Calculate(new[] { basic, hra, conveyance }, 600_000m);

        var basicLine = result.Single(r => r.Code == "BASIC");
        var hraLine = result.Single(r => r.Code == "HRA");
        var convLine = result.Single(r => r.Code == "CONV");

        basicLine.AnnualAmount.Should().Be(240_000m);   // 40% of 600,000
        basicLine.MonthlyAmount.Should().Be(20_000m);   // /12
        hraLine.AnnualAmount.Should().Be(48_000m);      // 20% of 240,000
        convLine.AnnualAmount.Should().Be(24_000m);     // fixed
        convLine.MonthlyAmount.Should().Be(2_000m);
    }

    [Fact]
    public void Calculate_MonthlyAmount_IsAnnualDividedBy12()
    {
        var basic = Rule("BASIC", SalaryComponentType.Earning, CalculationMethod.Fixed, 120_000m, 1);

        var line = CtcBreakdownCalculator.Calculate(new[] { basic }, 120_000m).Single();

        line.MonthlyAmount.Should().Be(10_000m);
    }

    [Fact]
    public void Calculate_FormulaComponent_EvaluatesAgainstEarlierResolvedComponents()
    {
        // PF = 12% of Basic, expressed as a formula referencing the BASIC code.
        var basic = Rule("BASIC", SalaryComponentType.Earning, CalculationMethod.PercentageOfGross, 50m, 1);
        var pf = Rule("PF", SalaryComponentType.Statutory, CalculationMethod.Formula, null, 2, "BASIC * 0.12");

        var result = CtcBreakdownCalculator.Calculate(new[] { basic, pf }, 1_000_000m);

        result.Single(r => r.Code == "BASIC").AnnualAmount.Should().Be(500_000m);
        result.Single(r => r.Code == "PF").AnnualAmount.Should().Be(60_000m); // 12% of 500,000
    }

    [Fact]
    public void Calculate_FormulaUsingGrossVariable_UsesAnnualCtc()
    {
        var allowance = Rule("ALLOW", SalaryComponentType.Earning, CalculationMethod.Formula, null, 1, "gross * 0.1");

        var line = CtcBreakdownCalculator.Calculate(new[] { allowance }, 800_000m).Single();

        line.AnnualAmount.Should().Be(80_000m);
    }

    [Fact]
    public void Calculate_Override_UsesSuppliedAmount_AndFlagsOverride()
    {
        var hra = Rule("HRA", SalaryComponentType.Earning, CalculationMethod.PercentageOfBasic, 20m, 2);
        var basic = Rule("BASIC", SalaryComponentType.Earning, CalculationMethod.PercentageOfGross, 40m, 1);

        var overrides = new Dictionary<Guid, decimal> { [hra.ComponentId] = 99_999m };

        var result = CtcBreakdownCalculator.Calculate(new[] { basic, hra }, 600_000m, overrides);

        var hraLine = result.Single(r => r.Code == "HRA");
        hraLine.AnnualAmount.Should().Be(99_999m);
        hraLine.IsOverride.Should().BeTrue();

        // The non-overridden component is unaffected.
        result.Single(r => r.Code == "BASIC").IsOverride.Should().BeFalse();
    }

    [Fact]
    public void Calculate_RespectsProcessingOrder_SoBasicResolvesBeforePercentOfBasic()
    {
        // HRA listed first by input but a higher processing order than BASIC — must still resolve after BASIC.
        var hra = Rule("HRA", SalaryComponentType.Earning, CalculationMethod.PercentageOfBasic, 50m, 2);
        var basic = Rule("BASIC", SalaryComponentType.Earning, CalculationMethod.PercentageOfGross, 40m, 1);

        var result = CtcBreakdownCalculator.Calculate(new[] { hra, basic }, 500_000m);

        result.Single(r => r.Code == "HRA").AnnualAmount.Should().Be(100_000m); // 50% of (40% of 500,000 = 200,000)
    }

    [Fact]
    public void TotalEarnings_SumsOnlyEarningComponents()
    {
        var basic = Rule("BASIC", SalaryComponentType.Earning, CalculationMethod.Fixed, 300_000m, 1);
        var hra = Rule("HRA", SalaryComponentType.Earning, CalculationMethod.Fixed, 100_000m, 2);
        var pf = Rule("PF", SalaryComponentType.Statutory, CalculationMethod.Fixed, 36_000m, 3);

        var result = CtcBreakdownCalculator.Calculate(new[] { basic, hra, pf }, 400_000m);

        CtcBreakdownCalculator.TotalEarnings(result).Should().Be(400_000m); // PF excluded
    }

    [Fact]
    public void Calculate_RoundsToTwoDecimalPlaces()
    {
        var basic = Rule("BASIC", SalaryComponentType.Earning, CalculationMethod.PercentageOfGross, 33.333m, 1);

        var line = CtcBreakdownCalculator.Calculate(new[] { basic }, 100_000m).Single();

        // 33.333% of 100,000 = 33,333.00 (rounded to 2 dp)
        line.AnnualAmount.Should().Be(33_333.00m);
    }
}
