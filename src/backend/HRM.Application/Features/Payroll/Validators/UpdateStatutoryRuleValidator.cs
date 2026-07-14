using FluentValidation;
using HRM.Application.Features.Payroll.Commands;

namespace HRM.Application.Features.Payroll.Validators;

/// <summary>
/// Structural validation for updating a statutory rule (US-PAY-006 FR-1/FR-2/FR-6). The rule type is fixed
/// at create time, so the update command can carry EITHER slabs OR social-security; when slabs are present
/// they must be contiguous (FR-6). The service performs the type-aware shape check against the stored rule.
/// </summary>
public sealed class UpdateStatutoryRuleValidator : AbstractValidator<UpdateStatutoryRuleCommand>
{
    public UpdateStatutoryRuleValidator()
    {
        RuleFor(x => x.RuleName)
            .NotEmpty().WithMessage("Rule name is required.")
            .MaximumLength(100).WithMessage("Rule name cannot exceed 100 characters.");

        RuleFor(x => x.CountryCode)
            .NotEmpty().WithMessage("Country code is required.")
            .MaximumLength(5).WithMessage("Country code cannot exceed 5 characters.");

        RuleFor(x => x.FiscalYear)
            .NotEmpty().WithMessage("Fiscal year is required.")
            .MaximumLength(10).WithMessage("Fiscal year cannot exceed 10 characters.");

        RuleFor(x => x.EffectiveTo)
            .Must((cmd, to) => to is null || to.Value >= cmd.EffectiveFrom)
            .WithMessage("Effective-to date must be on or after the effective-from date.");

        // FR-6: whenever slabs are supplied they must be contiguous.
        When(x => x.TaxSlabs is { Count: > 0 }, () =>
        {
            RuleFor(x => x.TaxSlabs)
                .Must(StatutorySlabRules.AreContiguous)
                .WithMessage(StatutorySlabRules.ContiguityMessage);

            RuleForEach(x => x.TaxSlabs).SetValidator(new TaxSlabInputValidator());
        });

        When(x => x.SocialSecurity is not null, () =>
            RuleFor(x => x.SocialSecurity!).SetValidator(new SocialSecurityInputValidator()));

        // TAX-2: configurable exemptions — same per-item checks as create (the service re-checks type-awareness
        // against the stored rule, so income-tax-only is enforced there for the fixed rule type).
        When(x => x.Exemptions is not null, () =>
            RuleForEach(x => x.Exemptions!).SetValidator(new ExemptionInputValidator()));
    }
}
