using FluentValidation;
using HRM.Application.Features.Roles.Commands;

namespace HRM.Application.Features.Roles.Validators;

public sealed class AssignUserRolesValidator : AbstractValidator<AssignUserRolesCommand>
{
    public AssignUserRolesValidator()
    {
        RuleFor(x => x.UserTenantId)
            .NotEmpty().WithMessage("User tenant membership ID is required.");

        RuleFor(x => x.RoleIds)
            .NotNull().WithMessage("Role IDs list is required.")
            .Must(ids => ids.Count > 0).WithMessage("At least one role must be assigned.");
    }
}
