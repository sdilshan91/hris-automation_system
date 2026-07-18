using FluentValidation;
using HRM.Application.Features.Auth.Commands;

namespace HRM.Application.Features.Auth.Validators;

/// <summary>
/// Validates authenticated change-password command input (US-AUTH-004, ISSUE-248). The new-password complexity
/// rules mirror <see cref="ResetPasswordValidator"/>; the tenant-specific policy + history are enforced downstream
/// in the shared apply path.
/// </summary>
public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(12).WithMessage("Password must be at least 12 characters.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.")
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must be different from your current password.");
    }
}
