using FluentValidation;
using HRM.Application.Features.LeaveEntitlements.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.LeaveEntitlements.Validators;

public sealed class CreateLeaveEntitlementRuleValidator : AbstractValidator<CreateLeaveEntitlementRuleCommand>
{
    private static readonly HashSet<string> s_validEmploymentTypes =
        new(Enum.GetNames<EmploymentType>(), StringComparer.OrdinalIgnoreCase);

    public CreateLeaveEntitlementRuleValidator()
    {
        RuleFor(x => x.LeaveTypeId)
            .NotEmpty().WithMessage("Leave type ID is required.");

        RuleFor(x => x.EntitlementDays)
            .GreaterThanOrEqualTo(0).WithMessage("Entitlement days must be zero or positive.");

        RuleFor(x => x.Priority)
            .GreaterThanOrEqualTo(0).WithMessage("Priority must be zero or positive.");

        RuleFor(x => x.EffectiveFrom)
            .NotEmpty().WithMessage("Effective from date is required.");

        RuleFor(x => x.EffectiveTo)
            .GreaterThan(x => x.EffectiveFrom)
            .When(x => x.EffectiveTo.HasValue)
            .WithMessage("Effective to date must be after effective from date.");

        RuleFor(x => x.EmploymentType)
            .Must(BeValidEmploymentType!)
            .When(x => !string.IsNullOrWhiteSpace(x.EmploymentType))
            .WithMessage("Employment type must be one of: FullTime, PartTime, Contract, Intern.");

        RuleFor(x => x.TenureMinMonths)
            .GreaterThanOrEqualTo(0)
            .When(x => x.TenureMinMonths.HasValue)
            .WithMessage("Tenure min months must be zero or positive.");

        RuleFor(x => x.TenureMaxMonths)
            .GreaterThan(x => x.TenureMinMonths ?? 0)
            .When(x => x.TenureMaxMonths.HasValue && x.TenureMinMonths.HasValue)
            .WithMessage("Tenure max months must be greater than tenure min months.");
    }

    private static bool BeValidEmploymentType(string value)
        => s_validEmploymentTypes.Contains(value);
}
