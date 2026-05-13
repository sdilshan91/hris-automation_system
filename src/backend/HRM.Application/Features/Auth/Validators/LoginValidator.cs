using FluentValidation;
using HRM.Application.Features.Auth.Commands;

namespace HRM.Application.Features.Auth.Validators;

/// <summary>
/// Validates login command input.
/// </summary>
public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(150).WithMessage("Email cannot exceed 150 characters.")
            .EmailAddress().WithMessage("Email format is invalid.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");

        When(x => x.MfaCode is not null, () =>
        {
            RuleFor(x => x.MfaCode)
                .Length(6).WithMessage("MFA code must be 6 digits.")
                .Matches(@"^\d{6}$").WithMessage("MFA code must contain only digits.");
        });
    }
}
