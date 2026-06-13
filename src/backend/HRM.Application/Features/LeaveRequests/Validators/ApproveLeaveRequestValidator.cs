using FluentValidation;
using HRM.Application.Features.LeaveRequests.Commands;

namespace HRM.Application.Features.LeaveRequests.Validators;

/// <summary>
/// Shape-level validation for a leave-request approval (US-LV-005 FR-1, BR-2).
/// The comment is optional; scope/authorization, state and balance checks are enforced in
/// the service layer.
/// </summary>
public sealed class ApproveLeaveRequestValidator : AbstractValidator<ApproveLeaveRequestCommand>
{
    public ApproveLeaveRequestValidator()
    {
        RuleFor(x => x.LeaveRequestId)
            .NotEmpty().WithMessage("Leave request id is required.");

        RuleFor(x => x.Comment)
            .MaximumLength(2000).WithMessage("Comment must not exceed 2000 characters.");
    }
}
