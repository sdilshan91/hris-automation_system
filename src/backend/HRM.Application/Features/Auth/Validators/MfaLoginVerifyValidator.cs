using FluentValidation;
using HRM.Application.Features.Auth.Commands;

namespace HRM.Application.Features.Auth.Validators;

/// <summary>
/// Validates MFA login challenge verification command.
/// </summary>
public sealed class MfaLoginVerifyValidator : AbstractValidator<MfaLoginVerifyCommand>
{
    public MfaLoginVerifyValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(150).WithMessage("Email cannot exceed 150 characters.")
            .EmailAddress().WithMessage("Email format is invalid.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Verification code is required.");
    }
}
