// ============================================================================
// BUG-072 regression: monetary inputs on a statutory rule (tax-slab bounds,
// social-security wage ceiling) map to Postgres numeric(18,2). A value above
// that precision must be rejected by validation (400), not overflow to a 500.
// ============================================================================

using System.Globalization;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Commands;
using HRM.Application.Features.Payroll.Validators;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

public sealed class StatutoryRuleAmountBoundsTests
{
    private const decimal OverMax = 1_000_000_000_000_000_000m;   // 1e18 — exceeds numeric(18,2)
    private const decimal WithinMax = 5_000_000m;

    [Fact]
    public void TaxSlab_SlabTo_AbovePrecisionMax_IsInvalid()
    {
        var result = new TaxSlabInputValidator().Validate(new TaxSlabInput(0m, OverMax, 10m, 0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("maximum allowed"));
    }

    [Fact]
    public void TaxSlab_SlabFrom_AbovePrecisionMax_IsInvalid()
    {
        var result = new TaxSlabInputValidator().Validate(new TaxSlabInput(OverMax, null, 10m, 0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("maximum allowed"));
    }

    [Fact]
    public void TaxSlab_WithinPrecision_IsValid()
    {
        var result = new TaxSlabInputValidator().Validate(new TaxSlabInput(0m, WithinMax, 10m, 0));

        result.IsValid.Should().BeTrue();
    }

    // ── ISSUE-169: the rate column is numeric(5,2). An over-precise rate used to pass validation, get
    //    ROUNDED by Postgres on insert, and come back as a DIFFERENT number with no error or warning.
    [Theory]
    [Trait("Issue", "ISSUE-169")]
    [InlineData("12.345")]
    [InlineData("0.001")]
    [InlineData("99.999")]
    public void TaxSlab_RatePercentage_MoreThanTwoDecimals_IsInvalid(string rate)
    {
        var result = new TaxSlabInputValidator().Validate(
            new TaxSlabInput(0m, 100_000m, decimal.Parse(rate, CultureInfo.InvariantCulture), 0));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorCode == "invalid_rate_scale" && e.ErrorMessage.Contains("2 decimal places"));
    }

    // Do NOT over-tighten: 2dp, 1dp, whole and trailing-zero rates all remain valid.
    [Theory]
    [Trait("Issue", "ISSUE-169")]
    [InlineData("12.34")]
    [InlineData("12.5")]
    [InlineData("10")]
    [InlineData("12.3400")]  // ignoreTrailingZeros: scale 4 but only 2 significant decimals
    [InlineData("0")]
    [InlineData("100")]
    public void TaxSlab_RatePercentage_TwoDecimalsOrFewer_IsValid(string rate)
    {
        var result = new TaxSlabInputValidator().Validate(
            new TaxSlabInput(0m, 100_000m, decimal.Parse(rate, CultureInfo.InvariantCulture), 0));

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    // ISSUE-169 (wiring): TaxSlabInputValidator is reached through BOTH the create and the update command
    //  validators (CreateStatutoryRuleValidator / UpdateStatutoryRuleValidator), so both entry points must
    //  reject the over-precise rate — not just the leaf validator in isolation.
    private static IReadOnlyList<TaxSlabInput> SlabsWithRate(decimal rate) =>
    [
        new(0m, 250_000m, 0m, 0),
        new(250_000m, null, rate, 1),
    ];

    [Fact]
    [Trait("Issue", "ISSUE-169")]
    public void CreateCommand_WithOverPreciseSlabRate_IsInvalid()
    {
        var cmd = new CreateStatutoryRuleCommand(
            StatutoryRuleType.IncomeTax, "PAYE", "LK", "2026-2027",
            new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true,
            SlabsWithRate(12.345m), null);

        var result = new CreateStatutoryRuleValidator().Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorCode == "invalid_rate_scale" && e.ErrorMessage.Contains("2 decimal places"));
    }

    [Fact]
    [Trait("Issue", "ISSUE-169")]
    public void CreateCommand_WithTwoDecimalSlabRate_IsValid()
    {
        var cmd = new CreateStatutoryRuleCommand(
            StatutoryRuleType.IncomeTax, "PAYE", "LK", "2026-2027",
            new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true,
            SlabsWithRate(12.34m), null);

        var result = new CreateStatutoryRuleValidator().Validate(cmd);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    [Trait("Issue", "ISSUE-169")]
    public void UpdateCommand_WithOverPreciseSlabRate_IsInvalid()
    {
        var cmd = new UpdateStatutoryRuleCommand(
            Guid.NewGuid(), "PAYE", "LK", "2026-2027",
            new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true,
            SlabsWithRate(12.345m), null);

        var result = new UpdateStatutoryRuleValidator().Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorCode == "invalid_rate_scale" && e.ErrorMessage.Contains("2 decimal places"));
    }

    [Fact]
    public void SocialSecurity_WageCeiling_AbovePrecisionMax_IsInvalid()
    {
        var result = new SocialSecurityInputValidator().Validate(
            new SocialSecurityInputDto(5m, 5m, OverMax, StatutoryApplicableOn.Gross, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("maximum allowed"));
    }
}
