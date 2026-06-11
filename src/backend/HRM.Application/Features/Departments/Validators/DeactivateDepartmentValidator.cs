using FluentValidation;
using HRM.Application.Features.Departments.Commands;

namespace HRM.Application.Features.Departments.Validators;

public sealed class DeactivateDepartmentValidator : AbstractValidator<DeactivateDepartmentCommand>
{
    public DeactivateDepartmentValidator()
    {
        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department ID is required.");
    }
}
