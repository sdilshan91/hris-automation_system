// ============================================================================
// US-PAY-001 / BUG-061 — FluentValidation rules for CreateSalaryComponentCommand /
// UpdateSalaryComponentCommand, focused on the Fixed-value non-negativity rule (BUG-061) while proving the
// existing percentage bounds are not regressed. Pure validator unit tests (no InMemory / DB).
// ============================================================================

using System.Globalization;
using FluentValidation.TestHelper;
using HRM.Application.Features.Payroll.Commands;
using HRM.Application.Features.Payroll.Validators;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-PAY-001")]
public sealed class SalaryComponentValidatorTests
{
    private readonly CreateSalaryComponentValidator _create = new();
    private readonly UpdateSalaryComponentValidator _update = new();

    private static CreateSalaryComponentCommand Create(
        CalculationMethod method = CalculationMethod.Fixed,
        decimal? defaultValue = 1000m,
        string? formula = null) => new(
        Name: "Basic",
        Code: "BASIC",
        Type: SalaryComponentType.Earning,
        CalculationMethod: method,
        DefaultValue: defaultValue,
        FormulaExpression: formula,
        IsTaxable: true,
        IsStatutory: false,
        IsActive: true,
        ProcessingOrder: 1);

    private static UpdateSalaryComponentCommand Update(
        CalculationMethod method = CalculationMethod.Fixed,
        decimal? defaultValue = 1000m) => new(
        ComponentId: Guid.NewGuid(),
        Name: "Basic",
        Code: "BASIC",
        Type: SalaryComponentType.Earning,
        CalculationMethod: method,
        DefaultValue: defaultValue,
        FormulaExpression: null,
        IsTaxable: true,
        IsStatutory: false,
        IsActive: true,
        ProcessingOrder: 1);

    // ── BUG-061: a negative Fixed value is rejected ──────────────────────────────

    [Fact]
    public void Create_NegativeFixedValue_Fails()
        => _create.TestValidate(Create(defaultValue: -1m))
            .ShouldHaveValidationErrorFor(x => x.DefaultValue!.Value)
            .WithErrorCode("negative_fixed_value");

    [Fact]
    public void Update_NegativeFixedValue_Fails()
        => _update.TestValidate(Update(defaultValue: -0.01m))
            .ShouldHaveValidationErrorFor(x => x.DefaultValue!.Value)
            .WithErrorCode("negative_fixed_value");

    [Theory]
    [InlineData(0)]
    [InlineData(0.01)]
    [InlineData(50_000)]
    public void Create_ZeroOrPositiveFixedValue_Passes(double value)
        => _create.TestValidate(Create(defaultValue: (decimal)value))
            .ShouldNotHaveValidationErrorFor(x => x.DefaultValue!.Value);

    [Theory]
    [InlineData(0)]
    [InlineData(50_000)]
    public void Update_ZeroOrPositiveFixedValue_Passes(double value)
        => _update.TestValidate(Update(defaultValue: (decimal)value))
            .ShouldNotHaveValidationErrorFor(x => x.DefaultValue!.Value);

    // ── Regression: percentage bounds (0-100) still enforced ─────────────────────

    [Fact]
    public void Create_PercentageAbove100_Fails()
        => _create.TestValidate(Create(method: CalculationMethod.PercentageOfBasic, defaultValue: 150m))
            .ShouldHaveValidationErrorFor(x => x.DefaultValue!.Value);

    [Fact]
    public void Create_NegativePercentage_Fails()
        => _create.TestValidate(Create(method: CalculationMethod.PercentageOfGross, defaultValue: -5m))
            .ShouldHaveValidationErrorFor(x => x.DefaultValue!.Value);

    [Fact]
    public void Create_ValidPercentage_Passes()
        => _create.TestValidate(Create(method: CalculationMethod.PercentageOfBasic, defaultValue: 12m))
            .ShouldNotHaveValidationErrorFor(x => x.DefaultValue!.Value);

    // ── ISSUE-369: the numeric(18,2) money contract on DefaultValue ──────────────
    //
    // default_value is numeric(18,2), so Postgres ROUNDS anything finer on write and the caller was never
    // told. Rejecting is the honest answer; the service's post-save re-read (SalaryComponentPrecision-
    // PostgresTests) is the belt-and-braces behind it. Same contract as ISSUE-152 on AnnualCtc.

    [Fact]
    public void Create_DefaultValueWithMoreThanTwoDecimalPlaces_Fails()
        => _create.TestValidate(Create(defaultValue: 1234.5678m))
            .ShouldHaveValidationErrorFor(x => x.DefaultValue!.Value)
            .WithErrorCode("invalid_default_value_scale")
            .WithErrorMessage("Default value cannot have more than 2 decimal places.");

    [Fact]
    public void Update_DefaultValueWithMoreThanTwoDecimalPlaces_Fails()
        => _update.TestValidate(Update(defaultValue: 1234.5678m))
            .ShouldHaveValidationErrorFor(x => x.DefaultValue!.Value)
            .WithErrorCode("invalid_default_value_scale")
            .WithErrorMessage("Default value cannot have more than 2 decimal places.");

    [Fact]
    public void Create_PercentageWithMoreThanTwoDecimalPlaces_Fails()
        => _create.TestValidate(Create(method: CalculationMethod.PercentageOfBasic, defaultValue: 12.345m))
            .ShouldHaveValidationErrorFor(x => x.DefaultValue!.Value)
            .WithErrorCode("invalid_default_value_scale");

    /// <summary>ignoreTrailingZeros: 1234.50 is a 2-dp amount, not an over-precise one.</summary>
    [Theory]
    [InlineData("1234.50")]
    [InlineData("1234.5000")]
    [InlineData("1234.5")]
    [InlineData("1234")]
    [InlineData("0.01")]
    public void Create_DefaultValueWithinTwoDecimalPlaces_Passes(string value)
        => _create.TestValidate(Create(defaultValue: decimal.Parse(value, CultureInfo.InvariantCulture)))
            .ShouldNotHaveValidationErrorFor(x => x.DefaultValue!.Value);

    [Theory]
    [InlineData("1234.50")]
    [InlineData("1234.5000")]
    public void Update_DefaultValueWithinTwoDecimalPlaces_Passes(string value)
        => _update.TestValidate(Update(defaultValue: decimal.Parse(value, CultureInfo.InvariantCulture)))
            .ShouldNotHaveValidationErrorFor(x => x.DefaultValue!.Value);

    /// <summary>The Formula path discards DefaultValue entirely, so the scale rule must not fire there.</summary>
    [Fact]
    public void Create_FormulaMethod_IgnoresDefaultValueScale()
        => _create.TestValidate(Create(method: CalculationMethod.Formula, defaultValue: 1234.5678m, formula: "basic * 0.12"))
            .ShouldNotHaveValidationErrorFor(x => x.DefaultValue!.Value);
}
