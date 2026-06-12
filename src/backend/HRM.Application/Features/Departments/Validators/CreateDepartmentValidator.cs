using FluentValidation;
using HRM.Application.Features.Departments.Commands;

namespace HRM.Application.Features.Departments.Validators;

public sealed class CreateDepartmentValidator : AbstractValidator<CreateDepartmentCommand>
{
    public CreateDepartmentValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Department name is required.")
            .MaximumLength(150).WithMessage("Department name cannot exceed 150 characters.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Department code is required.")
            .MaximumLength(50).WithMessage("Department code cannot exceed 50 characters.")
            .Matches(@"^[A-Za-z0-9\-_]+$").WithMessage("Department code may only contain letters, digits, hyphens, and underscores.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters.");
    }
}
