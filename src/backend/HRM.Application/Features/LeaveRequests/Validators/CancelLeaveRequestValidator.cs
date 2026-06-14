using FluentValidation;
using HRM.Application.Features.LeaveRequests.Commands;

namespace HRM.Application.Features.LeaveRequests.Validators;

/// <summary>
/// Shape-level validation for a leave-request cancellation (US-LV-010 FR-1, BR-5).
/// The reason is conditionally mandatory (required for approved leaves, optional for pending) —
/// that decision depends on the request's current status, which the validator cannot see, so it
/// is enforced in the service. Here we only bound the reason length when present.
/// </summary>
public sealed class CancelLeaveRequestValidator : AbstractValidator<CancelLeaveRequestCommand>
{
    public CancelLeaveRequestValidator()
    {
        RuleFor(x => x.LeaveRequestId)
            .NotEmpty().WithMessage("Leave request id is required.");

        RuleFor(x => x.Reason)
            .MaximumLength(2000).WithMessage("Cancellation reason must not exceed 2000 characters.");
    }
}
