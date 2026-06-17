using FluentValidation;
using HRM.Application.Features.Users.Commands;

namespace HRM.Application.Features.Users.Validators;

/// <summary>US-ADM-005 AC-3/FR-4: the new role set must be non-empty (roles are replaced wholesale).</summary>
public sealed class EditUserRolesValidator : AbstractValidator<EditUserRolesCommand>
{
    public EditUserRolesValidator()
    {
        RuleFor(x => x.UserTenantId)
            .NotEmpty();

        RuleFor(x => x.RoleIds)
            .NotEmpty().WithMessage("At least one role must be assigned.");
    }
}
