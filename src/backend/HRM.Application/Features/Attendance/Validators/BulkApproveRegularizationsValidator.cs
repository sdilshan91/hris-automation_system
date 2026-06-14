using FluentValidation;
using HRM.Application.Features.Attendance.Commands;

namespace HRM.Application.Features.Attendance.Validators;

/// <summary>
/// Shape-level validation for a bulk regularization approval (US-ATT-004 BR-7).
/// At least one id is required; per-item authorization, state, and payroll-lock checks are enforced
/// in the service layer and reported per item.
/// </summary>
public sealed class BulkApproveRegularizationsValidator
    : AbstractValidator<BulkApproveRegularizationsCommand>
{
    public BulkApproveRegularizationsValidator()
    {
        RuleFor(x => x.RegularizationIds)
            .NotNull().WithMessage("At least one regularization id is required.")
            .Must(ids => ids is not null && ids.Count > 0)
            .WithMessage("At least one regularization id is required.")
            .Must(ids => ids is null || ids.Count <= 100)
            .WithMessage("A maximum of 100 regularizations can be approved in one action.");

        RuleFor(x => x.Comment)
            .MaximumLength(2000).WithMessage("Comment must not exceed 2000 characters.");
    }
}
