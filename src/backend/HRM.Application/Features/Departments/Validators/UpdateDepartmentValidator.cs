using FluentValidation;
using HRM.Application.Features.Departments.Commands;

namespace HRM.Application.Features.Departments.Validators;

public sealed class UpdateDepartmentValidator : AbstractValidator<UpdateDepartmentCommand>
{
    public UpdateDepartmentValidator()
    {
        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("Department ID is required.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Department name is required.")
            .MaximumLength(150).WithMessage("Department name cannot exceed 150 characters.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Department code is required.")
            .MaximumLength(50).WithMessage("Department code cannot exceed 50 characters.")
            .Matches(@"^[A-Za-z0-9\-_]+$").WithMessage("Department code may only contain letters, digits, hyphens, and underscores.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters.");

        // Prevent self-referencing (setting parent to self) at the validator level
        RuleFor(x => x)
            .Must(x => x.ParentDepartmentId != x.DepartmentId)
            .WithMessage("A department cannot be its own parent.")
            .When(x => x.ParentDepartmentId.HasValue);
    }
}
