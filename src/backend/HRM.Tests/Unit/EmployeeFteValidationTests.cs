// ============================================================================
// CAL-6 / US-CHR-013 AC-1 + AC-2: Fte and WorkArrangement validation on the create + profile-update paths.
//
// These bounds are load-bearing, not cosmetic. Fte multiplies leave entitlement (all three
// LeaveEntitlementService pro-rata sites) and, opt-in, the overtime hourly base:
//   - 0 or negative would zero / invert an entitlement,
//   - > 1.00 would silently OVER-accrue leave for everyone on the rule,
//   - the column is numeric(3,2), so a 3-dp value would be silently ROUNDED by Postgres rather than
//     rejected — the scale rule refuses it at the boundary instead.
//
// Both validators share `EmployeeFteRules.ValidFte`, so both are exercised here: a rule wired to only one of
// them would leave the other path open.
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.Employees.Commands;
using HRM.Application.Features.Employees.DTOs;
using HRM.Application.Features.Employees.Validators;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

public sealed class EmployeeFteValidationTests
{
    private readonly CreateEmployeeValidator _create = new();
    private readonly UpdateEmployeeProfileValidator _update = new();

    private static CreateEmployeeCommand NewCreate(decimal? fte = null, WorkArrangement? arrangement = null)
        => new(
            FirstName: "Ann", LastName: "Employee", Email: "ann@acme.test", Phone: null,
            DateOfBirth: new DateTime(1990, 1, 1), Gender: Gender.Female,
            DateOfJoining: new DateTime(2024, 1, 1),
            DepartmentId: Guid.NewGuid(), JobTitleId: Guid.NewGuid(),
            EmploymentType: EmploymentType.FullTime, Status: EmployeeStatus.Active,
            Location: null, LocationId: null, CustomFields: null, UserId: null,
            Fte: fte, WorkArrangement: arrangement);

    // ══ TC-CHR-327 — Fte validation (AC-1 negative / boundary) ══

    /// <summary>An FTE outside (0, 1.00] is rejected. 0 zeroes an entitlement; >1 over-accrues for everyone.</summary>
    [Theory]
    [Trait("TC", "TC-CHR-327")]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(-1.0)]
    [InlineData(1.01)]
    [InlineData(2.0)]
    public void Create_RejectsFteOutsideTheValidRange(double fte)
    {
        _create.TestValidate(NewCreate(fte: (decimal)fte))
            .ShouldHaveValidationErrorFor(x => x.Fte)
            .WithErrorMessage("FTE must be greater than 0 and at most 1.00.");
    }

    /// <summary>
    /// More than 2 decimal places is rejected rather than silently rounded. numeric(3,2) would store 0.333 as
    /// 0.33 — a quiet 1% entitlement shift the admin never asked for.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-CHR-327")]
    [InlineData(0.333)]
    [InlineData(0.125)]
    public void Create_RejectsFteWithMoreThanTwoDecimalPlaces(double fte)
    {
        _create.TestValidate(NewCreate(fte: (decimal)fte))
            .ShouldHaveValidationErrorFor(x => x.Fte)
            .WithErrorMessage("FTE cannot have more than 2 decimal places.");
    }

    /// <summary>The valid boundaries and the common part-time values are accepted.</summary>
    [Theory]
    [Trait("TC", "TC-CHR-327")]
    [InlineData(1.00)]
    [InlineData(0.50)]
    [InlineData(0.25)]
    [InlineData(0.01)]
    public void Create_AcceptsValidFte(double fte)
        => _create.TestValidate(NewCreate(fte: (decimal)fte)).ShouldNotHaveValidationErrorFor(x => x.Fte);

    /// <summary>
    /// An OMITTED Fte (null) must NOT error — the service defaults it to 1.00. Without the `When(HasValue)`
    /// guard every existing create call, which sends no FTE, would start failing validation.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-CHR-327")]
    public void Create_OmittedFte_IsValid()
        => _create.TestValidate(NewCreate(fte: null)).ShouldNotHaveValidationErrorFor(x => x.Fte);

    /// <summary>
    /// The UPDATE path shares `ValidFte` and must reject the same values — a rule wired to create only would
    /// leave the profile-edit path wide open.
    /// </summary>
    [Theory]
    [Trait("TC", "TC-CHR-327")]
    [InlineData(0.0)]
    [InlineData(1.01)]
    [InlineData(-0.5)]
    public void Update_RejectsFteOutsideTheValidRange(double fte)
    {
        var command = new UpdateEmployeeProfileCommand(
            Guid.NewGuid(),
            new UpdateEmployeeProfileRequest
            {
                EmploymentInfo = new EmploymentInfoUpdate { Fte = (decimal)fte },
            });

        _update.TestValidate(command)
            .ShouldHaveValidationErrorFor(x => x.Request.EmploymentInfo!.Fte);
    }

    /// <summary>The update path accepts a valid FTE — the rule must not reject legitimate part-time edits.</summary>
    [Fact]
    [Trait("TC", "TC-CHR-327")]
    public void Update_AcceptsValidFte()
    {
        var command = new UpdateEmployeeProfileCommand(
            Guid.NewGuid(),
            new UpdateEmployeeProfileRequest
            {
                EmploymentInfo = new EmploymentInfoUpdate { Fte = 0.50m },
            });

        _update.TestValidate(command)
            .ShouldNotHaveValidationErrorFor(x => x.Request.EmploymentInfo!.Fte);
    }

    // ══ TC-CHR-329 — WorkArrangement validation (AC-2 negative) ══

    /// <summary>An undefined enum value is rejected — a cast int must not reach the column.</summary>
    [Theory]
    [Trait("TC", "TC-CHR-329")]
    [InlineData(99)]
    [InlineData(-1)]
    [InlineData(3)]
    public void Create_RejectsUndefinedWorkArrangement(int raw)
    {
        _create.TestValidate(NewCreate(arrangement: (WorkArrangement)raw))
            .ShouldHaveValidationErrorFor(x => x.WorkArrangement);
    }

    /// <summary>Every DEFINED arrangement is accepted.</summary>
    [Theory]
    [Trait("TC", "TC-CHR-329")]
    [InlineData(WorkArrangement.OnSite)]
    [InlineData(WorkArrangement.Hybrid)]
    [InlineData(WorkArrangement.Remote)]
    public void Create_AcceptsDefinedWorkArrangements(WorkArrangement arrangement)
        => _create.TestValidate(NewCreate(arrangement: arrangement))
            .ShouldNotHaveValidationErrorFor(x => x.WorkArrangement);

    /// <summary>An OMITTED arrangement (null) is valid — the service defaults it to OnSite.</summary>
    [Fact]
    [Trait("TC", "TC-CHR-329")]
    public void Create_OmittedWorkArrangement_IsValid()
        => _create.TestValidate(NewCreate(arrangement: null))
            .ShouldNotHaveValidationErrorFor(x => x.WorkArrangement);
}
