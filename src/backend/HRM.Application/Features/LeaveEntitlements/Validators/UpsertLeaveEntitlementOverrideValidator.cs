using FluentValidation;
using HRM.Application.Features.LeaveEntitlements.Commands;

namespace HRM.Application.Features.LeaveEntitlements.Validators;

public sealed class UpsertLeaveEntitlementOverrideValidator : AbstractValidator<UpsertLeaveEntitlementOverrideCommand>
{
    public UpsertLeaveEntitlementOverrideValidator()
    {
        RuleFor(x => x.EmployeeId)
            .NotEmpty().WithMessage("Employee ID is required.");

        RuleFor(x => x.LeaveTypeId)
            .NotEmpty().WithMessage("Leave type ID is required.");

        RuleFor(x => x.LeaveYear)
            .InclusiveBetween(2000, 2100).WithMessage("Leave year must be between 2000 and 2100.");

        RuleFor(x => x.EntitlementDays)
            .GreaterThanOrEqualTo(0).WithMessage("Entitlement days must be zero or positive.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required for overrides.")
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters.");
    }
}
