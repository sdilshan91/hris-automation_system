using FluentValidation;
using HRM.Application.Features.Onboarding.Commands;

namespace HRM.Application.Features.Onboarding.Validators;

/// <summary>US-ONB-005 §7 input validation for initiating offboarding.</summary>
public sealed class InitiateOffboardingValidator : AbstractValidator<InitiateOffboardingCommand>
{
    public InitiateOffboardingValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty().WithMessage("Employee is required.");

        RuleFor(x => x.LastWorkingDay)
            .Must(d => d.Date >= DateTime.UtcNow.Date)
            .WithMessage("Last working day must be today or in the future.");

        RuleFor(x => x.Reason).IsInEnum().WithMessage("A valid reason is required.");

        RuleFor(x => x.Notes)
            .MaximumLength(2000).WithMessage("Notes cannot exceed 2000 characters.")
            .When(x => x.Notes is not null);
    }
}
