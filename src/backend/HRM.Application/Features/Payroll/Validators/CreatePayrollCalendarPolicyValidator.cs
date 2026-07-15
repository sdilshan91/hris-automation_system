using FluentValidation;
using HRM.Application.Features.Payroll.Commands;

namespace HRM.Application.Features.Payroll.Validators;

/// <summary>
/// Structural validation for configuring a payroll calendar policy version (US-ATT-011 AC-4 / CAL-5). The only
/// hard structural constraint is a sane effective date; the exclusion toggle is a valid boolean either way.
/// </summary>
public sealed class CreatePayrollCalendarPolicyValidator : AbstractValidator<CreatePayrollCalendarPolicyCommand>
{
    public CreatePayrollCalendarPolicyValidator()
    {
        RuleFor(x => x.EffectiveFrom)
            .NotEqual(default(DateOnly)).WithMessage("Effective-from date is required.");
    }
}
