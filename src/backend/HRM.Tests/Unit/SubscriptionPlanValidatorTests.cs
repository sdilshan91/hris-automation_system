// ============================================================================
// US-ADM-009: subscription-plan validator unit tests (FluentValidation).
// Covers the field-shape rules: code required + format (FR-3), name required, non-negative prices/trial/
// retention, currency length, unknown-module rejection (FR-6), and the override key/value rules (FR-4).
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.SubscriptionPlans.Commands;
using HRM.Application.Features.SubscriptionPlans.DTOs;
using HRM.Application.Features.SubscriptionPlans.Validators;
using HRM.Domain.Authorization;

namespace HRM.Tests.Unit;

public sealed class SubscriptionPlanValidatorTests
{
    private readonly CreatePlanCommandValidator _create = new();
    private readonly UpdatePlanCommandValidator _update = new();
    private readonly UpsertPlanLimitOverrideCommandValidator _override = new();

    private static PlanEditableFields ValidFields(
        IReadOnlyList<string>? modules = null,
        decimal priceMonthly = 99m,
        string currency = "USD",
        int trialDays = 14) => new(
        Name: "Growth",
        Description: null,
        PriceMonthly: priceMonthly,
        PriceYearly: 990m,
        Currency: currency,
        TrialDays: trialDays,
        IsPublic: true,
        MaxEmployees: 100,
        MaxStorageGb: 100,
        MaxApiCallsPerMonth: null,
        MaxEmailSendsPerMonth: null,
        MaxCustomRoles: null,
        MaxCustomFieldsPerEntity: null,
        MaxWorkflows: null,
        AuditLogRetentionDays: 365,
        SlaTier: "business",
        EnabledModules: modules ?? new[] { PlanModules.Leave },
        FeatureFlags: new PlanFeatureFlagsDto(false, false, false, false, false));

    [Fact]
    public void Create_ValidCommand_Passes()
        => _create.TestValidate(new CreatePlanCommand("growth", ValidFields()))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Create_EmptyCode_Fails()
        => _create.TestValidate(new CreatePlanCommand("", ValidFields()))
            .ShouldHaveValidationErrorFor(x => x.Code);

    [Theory]
    [InlineData("Growth")]    // uppercase
    [InlineData("growth_x")]  // underscore
    [InlineData("-growth")]   // leading hyphen
    [InlineData("growth ")]   // trailing space
    public void Create_InvalidCodeFormat_Fails(string code)
        => _create.TestValidate(new CreatePlanCommand(code, ValidFields()))
            .ShouldHaveValidationErrorFor(x => x.Code);

    [Fact]
    public void Create_UnknownModule_Fails()
        => _create.TestValidate(new CreatePlanCommand("growth", ValidFields(modules: new[] { "Telepathy" })))
            .ShouldHaveValidationErrorFor("EnabledModules");

    [Fact]
    public void Create_NegativePrice_Fails()
        => _create.TestValidate(new CreatePlanCommand("growth", ValidFields(priceMonthly: -1m)))
            .ShouldHaveValidationErrorFor("PriceMonthly");

    [Fact]
    public void Create_NegativeTrialDays_Fails()
        => _create.TestValidate(new CreatePlanCommand("growth", ValidFields(trialDays: -1)))
            .ShouldHaveValidationErrorFor("TrialDays");

    [Fact]
    public void Create_BadCurrencyLength_Fails()
        => _create.TestValidate(new CreatePlanCommand("growth", ValidFields(currency: "DOLLARS")))
            .ShouldHaveValidationErrorFor("Currency");

    [Fact]
    public void Update_ValidCommand_Passes()
        => _update.TestValidate(new UpdatePlanCommand(Guid.NewGuid(), ValidFields()))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Override_UnknownKey_Fails()
        => _override.TestValidate(new UpsertPlanLimitOverrideCommand(Guid.NewGuid(), "bad_key", 100, null))
            .ShouldHaveValidationErrorFor(x => x.LimitKey);

    [Fact]
    public void Override_NullValue_IsAllowed_Unlimited()
        => _override.TestValidate(new UpsertPlanLimitOverrideCommand(Guid.NewGuid(), PlanLimitKeys.MaxEmployees, null, null))
            .ShouldNotHaveValidationErrorFor(x => x.Value);

    [Fact]
    public void Override_NegativeValue_Fails()
        => _override.TestValidate(new UpsertPlanLimitOverrideCommand(Guid.NewGuid(), PlanLimitKeys.MaxEmployees, -5, null))
            .ShouldHaveValidationErrorFor(x => x.Value);
}
