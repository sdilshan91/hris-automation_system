using FluentValidation;
using HRM.Application.Features.Employees.Commands;

namespace HRM.Application.Features.Employees.Validators;

/// <summary>
/// Validates AssignManagerCommand (US-CHR-011).
/// Business-rule validations (cycle, active manager) are in the service layer.
/// </summary>
public sealed class AssignManagerValidator : AbstractValidator<AssignManagerCommand>
{
    public AssignManagerValidator()
    {
        RuleFor(x => x.EmployeeId)
            .NotEmpty()
            .WithMessage("Employee ID is required.");

        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .WithMessage("Reason must not exceed 500 characters.");
    }
}
