using FluentValidation;
using HRM.Application.Features.Users.Commands;

namespace HRM.Application.Features.Users.Validators;

/// <summary>US-ADM-005 FR-2: a single invite needs a valid email and at least one role.</summary>
public sealed class InviteUserValidator : AbstractValidator<InviteUserCommand>
{
    public InviteUserValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(320);

        RuleFor(x => x.RoleIds)
            .NotEmpty().WithMessage("At least one role must be selected.");
    }
}
