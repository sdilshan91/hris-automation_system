using FluentValidation;
using HRM.Application.Features.Attendance.Commands;

namespace HRM.Application.Features.Attendance.Validators;

/// <summary>
/// Shape-level validation for an overtime pre-approval submit (US-ATT-006 AC-2/FR-4). Date,
/// past-date and active-employee checks are enforced in the service.
/// </summary>
public sealed class SubmitOvertimePreApprovalValidator : AbstractValidator<SubmitOvertimePreApprovalCommand>
{
    public SubmitOvertimePreApprovalValidator()
    {
        RuleFor(x => x.Request.ExpectedHours)
            .GreaterThan(0).WithMessage("Expected overtime hours must be greater than zero.")
            .LessThanOrEqualTo(24).WithMessage("Expected overtime hours cannot exceed 24.");

        RuleFor(x => x.Request.Reason)
            .NotEmpty().WithMessage("A reason is required.")
            .Must(r => (r ?? string.Empty).Trim().Length >= 10)
            .WithMessage("The reason must be at least 10 characters.")
            .MaximumLength(2000).WithMessage("The reason must not exceed 2000 characters.");
    }
}
