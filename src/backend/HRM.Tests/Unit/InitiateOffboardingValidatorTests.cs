// ============================================================================
// US-ONB-005: InitiateOffboardingCommand validator tests (structural rules, §7).
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.Onboarding.Commands;
using HRM.Application.Features.Onboarding.Validators;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

public sealed class InitiateOffboardingValidatorTests
{
    private readonly InitiateOffboardingValidator _validator = new();

    private static InitiateOffboardingCommand Cmd(
        Guid? employeeId = null, DateOnly? lwd = null,
        OffboardingReason reason = OffboardingReason.Resignation, string? notes = null) =>
        new(employeeId ?? Guid.NewGuid(), lwd ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)), null, reason, notes);

    [Fact]
    public void Valid_command_passes()
    {
        var result = _validator.TestValidate(Cmd());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_employee_id_fails()
    {
        var result = _validator.TestValidate(Cmd(employeeId: Guid.Empty));
        result.ShouldHaveValidationErrorFor(x => x.EmployeeId);
    }

    [Fact]
    public void Last_working_day_in_the_past_fails()
    {
        var result = _validator.TestValidate(Cmd(lwd: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))));
        result.ShouldHaveValidationErrorFor(x => x.LastWorkingDay);
    }

    [Fact]
    public void Last_working_day_today_passes()
    {
        var result = _validator.TestValidate(Cmd(lwd: DateOnly.FromDateTime(DateTime.UtcNow)));
        result.ShouldNotHaveValidationErrorFor(x => x.LastWorkingDay);
    }

    [Fact]
    public void Invalid_reason_enum_fails()
    {
        var result = _validator.TestValidate(Cmd(reason: (OffboardingReason)99));
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void Notes_over_2000_chars_fails()
    {
        var result = _validator.TestValidate(Cmd(notes: new string('x', 2001)));
        result.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void Null_notes_passes()
    {
        var result = _validator.TestValidate(Cmd(notes: null));
        result.ShouldNotHaveValidationErrorFor(x => x.Notes);
    }
}
