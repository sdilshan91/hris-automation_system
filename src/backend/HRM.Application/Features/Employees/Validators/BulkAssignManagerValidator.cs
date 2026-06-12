using FluentValidation;
using HRM.Application.Features.Employees.Commands;

namespace HRM.Application.Features.Employees.Validators;

/// <summary>
/// Validates BulkAssignManagerCommand (US-CHR-011 FR-4, NFR-6).
/// </summary>
public sealed class BulkAssignManagerValidator : AbstractValidator<BulkAssignManagerCommand>
{
    public BulkAssignManagerValidator()
    {
        RuleFor(x => x.EmployeeIds)
            .NotEmpty()
            .WithMessage("At least one employee ID is required.");

        RuleFor(x => x.EmployeeIds.Count)
            .LessThanOrEqualTo(100)
            .WithMessage("Bulk assignment is limited to 100 employees per request.");

        RuleFor(x => x.ManagerEmployeeId)
            .NotEmpty()
            .WithMessage("Manager employee ID is required.");

        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .WithMessage("Reason must not exceed 500 characters.");
    }
}
