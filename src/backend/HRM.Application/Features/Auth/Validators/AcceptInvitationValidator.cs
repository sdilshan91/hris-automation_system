using FluentValidation;
using HRM.Application.Features.Auth.Commands;

namespace HRM.Application.Features.Auth.Validators;

/// <summary>
/// BUG-294: field-level validation for invitation redemption. The password rules mirror
/// <see cref="ResetPasswordValidator"/> exactly — an invitee's first password must clear the same bar as a
/// reset one. The tenant's configurable password policy is enforced separately and independently inside the
/// shared password rail, so this is the floor, not the whole check.
/// </summary>
public sealed class AcceptInvitationValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Invitation token is required.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(12).WithMessage("Password must be at least 12 characters.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character.");
    }
}
