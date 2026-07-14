// ============================================================================
// US-PAY-006: Statutory deduction calculator — unit tests (pure, no DB).
//
// Financially/legally sensitive math (NFR-3 >= 90% on the calc logic):
//   - GOLDEN progressive tax: income 750,000 over [0-250K@0%, 250K-500K@5%, 500K-1M@20%] => 62,500 (S11).
//   - GOLDEN EPF ceiling: basic 20,000, ceiling 15,000, rate 12% => 1,800 (not 2,400) (S11).
//   - BR-2 taxable income = gross - exempt - declared exemptions, floored at zero.
//   - BR-6 below-threshold skip => zero tax.
//   - Top (unbounded) slab, exact-boundary income, single-slab (flat) tax, employer-side contribution.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;

namespace HRM.Tests.Unit;

public sealed class StatutoryCalculatorTests
{
    private static readonly TaxBand[] GoldenBands =
    [
        new(0m, 250_000m, 0m),
        new(250_000m, 500_000m, 5m),
        new(500_000m, 1_000_000m, 20m),
    ];

    [Fact]
    public void ComputeProgressiveTax_GoldenDataset_Yields62500()
    {
        var tax = StatutoryCalculator.ComputeProgressiveTax(750_000m, GoldenBands);
        tax.Should().Be(62_500m);
    }

    [Fact]
    public void ComputeProgressiveTax_IncomeBelowFirstThreshold_IsZero()
        => StatutoryCalculator.ComputeProgressiveTax(200_000m, GoldenBands).Should().Be(0m);

    [Fact]
    public void ComputeProgressiveTax_ExactSlabBoundary_TaxesLowerSlabsOnly()
        => StatutoryCalculator.ComputeProgressiveTax(500_000m, GoldenBands).Should().Be(12_500m);

    [Fact]
    public void ComputeProgressiveTax_UnboundedTopSlab_TaxesAllExcess()
    {
        TaxBand[] bands = [new(0m, 250_000m, 0m), new(250_000m, null, 10m)];
        StatutoryCalculator.ComputeProgressiveTax(1_000_000m, bands).Should().Be(75_000m);
    }

    [Fact]
    public void ComputeProgressiveTax_SingleFlatSlab_AppliesFlatRate()
    {
        TaxBand[] bands = [new(0m, null, 15m)];
        StatutoryCalculator.ComputeProgressiveTax(100_000m, bands).Should().Be(15_000m);
    }

    [Fact]
    public void ComputeProgressiveTax_ZeroOrNegativeIncome_IsZero()
    {
        StatutoryCalculator.ComputeProgressiveTax(0m, GoldenBands).Should().Be(0m);
        StatutoryCalculator.ComputeProgressiveTax(-5_000m, GoldenBands).Should().Be(0m);
    }

    [Fact]
    public void ComputeProgressiveTax_UnorderedBands_AreSortedBeforeApplying()
    {
        TaxBand[] unordered = [new(500_000m, 1_000_000m, 20m), new(0m, 250_000m, 0m), new(250_000m, 500_000m, 5m)];
        StatutoryCalculator.ComputeProgressiveTax(750_000m, unordered).Should().Be(62_500m);
    }

    [Fact]
    public void ComputeIncomeTax_AtOrBelowThreshold_SkipsTax()
    {
        StatutoryCalculator.ComputeIncomeTax(300_000m, GoldenBands, taxExemptThreshold: 300_000m).Should().Be(0m);
        StatutoryCalculator.ComputeIncomeTax(299_999m, GoldenBands, taxExemptThreshold: 300_000m).Should().Be(0m);
    }

    [Fact]
    public void ComputeIncomeTax_AboveThreshold_TaxesFullTaxableIncome()
        => StatutoryCalculator.ComputeIncomeTax(750_000m, GoldenBands, taxExemptThreshold: 100_000m).Should().Be(62_500m);

    [Fact]
    public void ComputeTaxableIncome_SubtractsExemptAndDeclaredExemptions()
        => StatutoryCalculator.ComputeTaxableIncome(grossEarnings: 100_000m, exemptEarnings: 10_000m, declaredExemptions: 20_000m)
            .Should().Be(70_000m);

    [Fact]
    public void ComputeTaxableIncome_NeverNegative()
        => StatutoryCalculator.ComputeTaxableIncome(50_000m, 40_000m, 30_000m).Should().Be(0m);

    [Fact]
    public void ComputeSocialSecurity_GoldenEpfCeiling_Yields1800()
    {
        var result = StatutoryCalculator.ComputeSocialSecurity(new SocialSecurityInput(20_000m, 12m, 12m, 15_000m));
        result.EmployeeContribution.Should().Be(1_800m);
        result.EmployerContribution.Should().Be(1_800m);
    }

    [Fact]
    public void ComputeSocialSecurity_BaseBelowCeiling_UsesFullBase()
    {
        var result = StatutoryCalculator.ComputeSocialSecurity(new SocialSecurityInput(10_000m, 12m, 12m, 15_000m));
        result.EmployeeContribution.Should().Be(1_200m);
    }

    [Fact]
    public void ComputeSocialSecurity_NoCeiling_UsesFullBase()
    {
        var result = StatutoryCalculator.ComputeSocialSecurity(new SocialSecurityInput(20_000m, 12m, 0m, null));
        result.EmployeeContribution.Should().Be(2_400m);
        result.EmployerContribution.Should().Be(0m);
    }

    [Fact]
    public void ComputeSocialSecurity_DistinctEmployerRate_IsApplied()
    {
        var result = StatutoryCalculator.ComputeSocialSecurity(new SocialSecurityInput(25_000m, 0m, 3m, 20_000m));
        result.EmployeeContribution.Should().Be(0m);
        result.EmployerContribution.Should().Be(600m);
    }

    [Fact]
    public void ComputeSocialSecurity_NegativeBase_IsZero()
    {
        var result = StatutoryCalculator.ComputeSocialSecurity(new SocialSecurityInput(-1m, 12m, 12m, 15_000m));
        result.EmployeeContribution.Should().Be(0m);
        result.EmployerContribution.Should().Be(0m);
    }

    // ── TAX-2: ComputeExemption (per-period monthly amount) ──────────────────────

    [Fact]
    public void ComputeExemption_FlatAmount_PerPeriod_ReturnsValueAsIs()
        => StatutoryCalculator.ComputeExemption(ExemptionCalculationType.FlatAmount, 250_000m, 750_000m, null, null, isAnnual: false)
            .Should().Be(250_000m);

    [Fact]
    public void ComputeExemption_FlatAmount_Annual_IsDividedByTwelve()
        => StatutoryCalculator.ComputeExemption(ExemptionCalculationType.FlatAmount, 3_000_000m, 750_000m, null, null, isAnnual: true)
            .Should().Be(250_000m);

    [Fact]
    public void ComputeExemption_PercentOfGross_TakesPercentOfMonthlyGross()
        => StatutoryCalculator.ComputeExemption(ExemptionCalculationType.PercentOfGross, 20m, 750_000m, null, null, isAnnual: false)
            .Should().Be(150_000m);

    [Fact]
    public void ComputeExemption_PercentOfComponent_TakesPercentOfComponentAmount()
        => StatutoryCalculator.ComputeExemption(ExemptionCalculationType.PercentOfComponent, 10m, 750_000m, componentAmount: 400_000m, null, isAnnual: false)
            .Should().Be(40_000m);

    [Fact]
    public void ComputeExemption_PercentOfComponent_NullComponentAmount_IsZero()
        => StatutoryCalculator.ComputeExemption(ExemptionCalculationType.PercentOfComponent, 10m, 750_000m, componentAmount: null, null, isAnnual: false)
            .Should().Be(0m);

    [Fact]
    public void ComputeExemption_MaxAmount_CapsTheComputedExemption()
        => StatutoryCalculator.ComputeExemption(ExemptionCalculationType.PercentOfGross, 50m, 750_000m, null, maxAmount: 250_000m, isAnnual: false)
            .Should().Be(250_000m); // raw 375,000 capped at 250,000.

    [Fact]
    public void ComputeExemption_AnnualCap_IsAlsoDividedByTwelve()
        => StatutoryCalculator.ComputeExemption(ExemptionCalculationType.FlatAmount, 6_000_000m, 750_000m, null, maxAmount: 3_000_000m, isAnnual: true)
            .Should().Be(250_000m); // raw 6M/12=500k, cap 3M/12=250k → 250k.

    [Fact]
    public void ComputeExemption_UnderCap_ReturnsRaw()
        => StatutoryCalculator.ComputeExemption(ExemptionCalculationType.PercentOfGross, 10m, 750_000m, null, maxAmount: 250_000m, isAnnual: false)
            .Should().Be(75_000m); // raw 75,000 < cap → uncapped.
}
