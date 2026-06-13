using FluentValidation;
using HRM.Application.Features.LeaveEntitlements.Queries;

namespace HRM.Application.Features.LeaveEntitlements.Validators;

public sealed class ComputeEffectiveEntitlementValidator : AbstractValidator<ComputeEffectiveEntitlementQuery>
{
    public ComputeEffectiveEntitlementValidator()
    {
        RuleFor(x => x.EmployeeId)
            .NotEmpty().WithMessage("Employee ID is required.");

        RuleFor(x => x.LeaveTypeId)
            .NotEmpty().WithMessage("Leave type ID is required.");

        RuleFor(x => x.LeaveYear)
            .InclusiveBetween(2000, 2100).WithMessage("Leave year must be between 2000 and 2100.");
    }
}
