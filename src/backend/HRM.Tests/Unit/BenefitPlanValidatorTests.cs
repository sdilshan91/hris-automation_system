// ============================================================================
// US-TRN-002 — FluentValidation rules for CreateBenefitPlanCommand / UpdateBenefitPlanCommand, plus the pure
// plan status-transition matrix (BenefitPlanService.IsLegalTransition, AC-2/AC-3/AC-6). InMemory-free unit tests.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Benefits.Commands;
using HRM.Application.Features.Benefits.DTOs;
using HRM.Application.Features.Benefits.Validators;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-TRN-002")]
public sealed class BenefitPlanValidatorTests
{
    private readonly CreateBenefitPlanValidator _create = new();
    private readonly UpdateBenefitPlanValidator _update = new();

    private static CreateBenefitPlanCommand Create(
        string name = "Gold Health",
        string type = "Health",
        decimal? employerCost = null,
        decimal? employeeCost = null,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null,
        DateOnly? opens = null,
        DateOnly? closes = null) => new(new CreateBenefitPlanRequest
        {
            Name = name,
            Type = type,
            EmployerCost = employerCost,
            EmployeeCost = employeeCost,
            EffectiveFrom = effectiveFrom ?? new DateOnly(2026, 1, 1),
            EffectiveTo = effectiveTo,
            EnrollmentOpensAt = opens,
            EnrollmentClosesAt = closes,
        });

    [Fact]
    public void Create_EmptyName_Fails()
        => _create.TestValidate(Create(name: "")).ShouldHaveValidationErrorFor(x => x.Request.Name);

    [Fact]
    public void Create_InvalidType_Fails()
        => _create.TestValidate(Create(type: "Teleportation")).ShouldHaveValidationErrorFor(x => x.Request.Type);

    [Theory]
    [InlineData("Health")]
    [InlineData("Dental")]
    [InlineData("Vision")]
    [InlineData("Life")]
    [InlineData("Retirement")]
    [InlineData("Disability")]
    [InlineData("Other")]
    public void Create_ValidType_Passes(string type)
        => _create.TestValidate(Create(type: type)).ShouldNotHaveValidationErrorFor(x => x.Request.Type);

    [Fact]
    public void Create_NegativeEmployerCost_Fails()
        => _create.TestValidate(Create(employerCost: -1)).ShouldHaveValidationErrorFor(x => x.Request.EmployerCost);

    [Fact]
    public void Create_NegativeEmployeeCost_Fails()
        => _create.TestValidate(Create(employeeCost: -0.5m)).ShouldHaveValidationErrorFor(x => x.Request.EmployeeCost);

    [Fact]
    public void Create_ZeroCosts_Passes()
        => _create.TestValidate(Create(employerCost: 0, employeeCost: 0)).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Create_EffectiveToBeforeFrom_Fails()
        => _create.TestValidate(Create(effectiveFrom: new DateOnly(2026, 7, 10), effectiveTo: new DateOnly(2026, 7, 1)))
            .ShouldHaveValidationErrorFor(x => x.Request);

    [Fact]
    public void Create_EffectiveToAfterFrom_Passes()
        => _create.TestValidate(Create(effectiveFrom: new DateOnly(2026, 7, 1), effectiveTo: new DateOnly(2026, 7, 31)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Create_EnrollmentClosesBeforeOpens_Fails()
        => _create.TestValidate(Create(opens: new DateOnly(2026, 7, 10), closes: new DateOnly(2026, 7, 1)))
            .ShouldHaveValidationErrorFor(x => x.Request);

    [Fact]
    public void Update_EmptyName_Fails()
    {
        var cmd = new UpdateBenefitPlanCommand(Guid.NewGuid(), new UpdateBenefitPlanRequest { Name = "", Type = "Health" });
        _update.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.Name);
    }

    // ── Status-transition matrix (AC-2/AC-3/AC-6) ───────────────────────────────

    [Theory]
    [InlineData(BenefitPlanStatus.Draft, BenefitPlanStatus.Active, true)]
    [InlineData(BenefitPlanStatus.Draft, BenefitPlanStatus.Archived, true)]
    [InlineData(BenefitPlanStatus.Draft, BenefitPlanStatus.Inactive, false)]
    [InlineData(BenefitPlanStatus.Active, BenefitPlanStatus.Inactive, true)]
    [InlineData(BenefitPlanStatus.Active, BenefitPlanStatus.Archived, true)]
    [InlineData(BenefitPlanStatus.Active, BenefitPlanStatus.Draft, false)]
    [InlineData(BenefitPlanStatus.Inactive, BenefitPlanStatus.Active, true)]
    [InlineData(BenefitPlanStatus.Inactive, BenefitPlanStatus.Archived, true)]
    [InlineData(BenefitPlanStatus.Inactive, BenefitPlanStatus.Draft, false)]
    [InlineData(BenefitPlanStatus.Archived, BenefitPlanStatus.Active, false)]
    [InlineData(BenefitPlanStatus.Archived, BenefitPlanStatus.Inactive, false)]
    [InlineData(BenefitPlanStatus.Archived, BenefitPlanStatus.Draft, false)]
    public void IsLegalTransition_MatchesMatrix(BenefitPlanStatus from, BenefitPlanStatus to, bool expected)
        => BenefitPlanService.IsLegalTransition(from, to).Should().Be(expected);
}
