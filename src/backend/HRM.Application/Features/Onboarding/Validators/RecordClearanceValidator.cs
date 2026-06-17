using FluentValidation;
using HRM.Application.Features.Onboarding.Commands;

namespace HRM.Application.Features.Onboarding.Validators;

/// <summary>US-ONB-005 §7 input validation for recording a department clearance decision.</summary>
public sealed class RecordClearanceValidator : AbstractValidator<RecordClearanceCommand>
{
    public RecordClearanceValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty().WithMessage("Task is required.");
        RuleFor(x => x.Status).IsInEnum().WithMessage("A valid clearance status is required.");

        RuleFor(x => x.Remarks)
            .MaximumLength(1000).WithMessage("Remarks cannot exceed 1000 characters.")
            .When(x => x.Remarks is not null);
    }
}
