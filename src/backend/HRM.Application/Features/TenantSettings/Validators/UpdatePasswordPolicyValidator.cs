using FluentValidation;
using HRM.Application.Features.TenantSettings.Commands;

namespace HRM.Application.Features.TenantSettings.Validators;

/// <summary>US-ADM-006 AC-4/FR-5: validate the password-policy update (bounds only; the policy itself is stored).</summary>
public sealed class UpdatePasswordPolicyValidator : AbstractValidator<UpdatePasswordPolicyCommand>
{
    public UpdatePasswordPolicyValidator()
    {
        RuleFor(x => x.Request.MinLength)
            .InclusiveBetween(8, 128).WithMessage("Minimum password length must be between 8 and 128.");

        RuleFor(x => x.Request.HistoryCount)
            .InclusiveBetween(0, 24).WithMessage("Password history count must be between 0 and 24.");

        RuleFor(x => x.Request.MaxAgeDays!.Value)
            .InclusiveBetween(1, 3650).WithMessage("Password max age must be between 1 and 3650 days.")
            .When(x => x.Request.MaxAgeDays.HasValue);
    }
}
