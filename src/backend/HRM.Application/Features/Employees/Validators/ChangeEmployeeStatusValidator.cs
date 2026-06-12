using FluentValidation;
using HRM.Application.Features.Employees.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Employees.Validators;

/// <summary>
/// FluentValidation validator for ChangeEmployeeStatusCommand (US-CHR-009 FR-3).
/// </summary>
public sealed class ChangeEmployeeStatusValidator : AbstractValidator<ChangeEmployeeStatusCommand>
{
    public ChangeEmployeeStatusValidator()
    {
        RuleFor(x => x.EmployeeId)
            .NotEmpty()
            .WithMessage("Employee ID is required.");

        RuleFor(x => x.NewStatus)
            .IsInEnum()
            .WithMessage("Invalid employee status value.");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("A reason is required for status changes.")
            .MaximumLength(1000)
            .WithMessage("Reason must not exceed 1000 characters.");

        RuleFor(x => x.EffectiveDate)
            .NotEmpty()
            .WithMessage("Effective date is required.");
    }
}
