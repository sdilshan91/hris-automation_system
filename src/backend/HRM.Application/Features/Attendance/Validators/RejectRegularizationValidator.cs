using FluentValidation;
using HRM.Application.Features.Attendance.Commands;

namespace HRM.Application.Features.Attendance.Validators;

/// <summary>
/// Shape-level validation for a regularization rejection (US-ATT-004 FR-3, BR-1).
/// The reason is mandatory, minimum 10 characters (trimmed). Authorization, state, and self-approval
/// checks are enforced in the service layer.
/// </summary>
public sealed class RejectRegularizationValidator : AbstractValidator<RejectRegularizationCommand>
{
    public RejectRegularizationValidator()
    {
        RuleFor(x => x.RegularizationId)
            .NotEmpty().WithMessage("Regularization id is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A rejection reason is required.")
            .Must(r => (r ?? string.Empty).Trim().Length >= 10)
            .WithMessage("The rejection reason must be at least 10 characters.")
            .MaximumLength(2000).WithMessage("The rejection reason must not exceed 2000 characters.");
    }
}
