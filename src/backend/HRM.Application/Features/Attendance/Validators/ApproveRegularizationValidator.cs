using FluentValidation;
using HRM.Application.Features.Attendance.Commands;

namespace HRM.Application.Features.Attendance.Validators;

/// <summary>
/// Shape-level validation for a regularization approval (US-ATT-004 AC-1, BR-2).
/// The comment is optional; authorization, state, self-approval, and payroll-lock checks are
/// enforced in the service layer.
/// </summary>
public sealed class ApproveRegularizationValidator : AbstractValidator<ApproveRegularizationCommand>
{
    public ApproveRegularizationValidator()
    {
        RuleFor(x => x.RegularizationId)
            .NotEmpty().WithMessage("Regularization id is required.");

        RuleFor(x => x.Comment)
            .MaximumLength(2000).WithMessage("Comment must not exceed 2000 characters.");
    }
}
