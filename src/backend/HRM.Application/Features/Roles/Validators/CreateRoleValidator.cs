using FluentValidation;
using HRM.Application.Features.Roles.Commands;
using HRM.Domain.Authorization;

namespace HRM.Application.Features.Roles.Validators;

public sealed class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Role name is required.")
            .MaximumLength(100).WithMessage("Role name cannot exceed 100 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.");

        RuleFor(x => x.Permissions)
            .NotNull().WithMessage("Permissions list is required.");

        RuleForEach(x => x.Permissions)
            .Must(p => PermissionCatalog.IsValid(p))
            .WithMessage("'{PropertyValue}' is not a valid permission.");
    }
}
