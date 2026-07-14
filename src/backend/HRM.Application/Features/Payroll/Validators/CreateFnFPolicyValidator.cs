using FluentValidation;
using HRM.Application.Features.Payroll.Commands;

namespace HRM.Application.Features.Payroll.Validators;

/// <summary>
/// Structural validation for configuring a Full-and-Final settlement policy version (ISSUE-294 Phase 1). The
/// only hard structural constraint is a sane effective date; the include toggles are all valid booleans.
/// </summary>
public sealed class CreateFnFPolicyValidator : AbstractValidator<CreateFnFPolicyCommand>
{
    public CreateFnFPolicyValidator()
    {
        RuleFor(x => x.EffectiveFrom)
            .NotEqual(default(DateOnly)).WithMessage("Effective-from date is required.");
    }
}
