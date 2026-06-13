using FluentValidation;
using HRM.Application.Features.LeaveRequests.Commands;

namespace HRM.Application.Features.LeaveRequests.Validators;

/// <summary>
/// Shape-level validation for a leave-request rejection (US-LV-005 BR-2, AC-2).
/// The rejection reason is mandatory; scope/authorization and state checks are enforced in
/// the service layer.
/// </summary>
public sealed class RejectLeaveRequestValidator : AbstractValidator<RejectLeaveRequestCommand>
{
    public RejectLeaveRequestValidator()
    {
        RuleFor(x => x.LeaveRequestId)
            .NotEmpty().WithMessage("Leave request id is required.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A rejection reason is required.")
            .MaximumLength(2000).WithMessage("Rejection reason must not exceed 2000 characters.");
    }
}
