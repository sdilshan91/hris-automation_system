// ============================================================================
// BUG-072 regression: monetary inputs on a statutory rule (tax-slab bounds,
// social-security wage ceiling) map to Postgres numeric(18,2). A value above
// that precision must be rejected by validation (400), not overflow to a 500.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
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

    [Fact]
    public void SocialSecurity_WageCeiling_AbovePrecisionMax_IsInvalid()
    {
        var result = new SocialSecurityInputValidator().Validate(
            new SocialSecurityInputDto(5m, 5m, OverMax, StatutoryApplicableOn.Gross, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("maximum allowed"));
    }
}
