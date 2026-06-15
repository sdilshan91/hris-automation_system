using FluentValidation;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Payroll.Validators;

/// <summary>
/// Structural validation for creating a statutory rule (US-PAY-006 FR-1/FR-2/FR-6). The headline rule is
/// FR-6: tax slabs must be contiguous — no gaps, no overlaps — enforced by <see cref="StatutorySlabRules"/>
/// (shared with the update validator). Income-tax-vs-social-security shape is also checked here and
/// re-checked defensively in the service.
/// </summary>
public sealed class CreateStatutoryRuleValidator : AbstractValidator<CreateStatutoryRuleCommand>
{
    public CreateStatutoryRuleValidator()
    {
        RuleFor(x => x.RuleType).IsInEnum().WithMessage("Rule type is invalid.");

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

        // FR-6 + shape: income tax → contiguous slabs and no social-security; others → social-security and no slabs.
        When(x => x.RuleType == StatutoryRuleType.IncomeTax, () =>
        {
            RuleFor(x => x.TaxSlabs)
                .NotEmpty().WithMessage("An income-tax rule requires at least one tax slab.")
                .Must(StatutorySlabRules.AreContiguous)
                .WithMessage(StatutorySlabRules.ContiguityMessage);

            RuleForEach(x => x.TaxSlabs).SetValidator(new TaxSlabInputValidator());

            RuleFor(x => x.SocialSecurity)
                .Null().WithMessage("An income-tax rule cannot carry social-security parameters.");
        });

        When(x => x.RuleType != StatutoryRuleType.IncomeTax, () =>
        {
            RuleFor(x => x.SocialSecurity)
                .NotNull().WithMessage("This rule type requires social-security parameters.");

            RuleFor(x => x.TaxSlabs)
                .Empty().WithMessage("Only an income-tax rule may carry tax slabs.");

            When(x => x.SocialSecurity is not null, () =>
                RuleFor(x => x.SocialSecurity!).SetValidator(new SocialSecurityInputValidator()));
        });
    }
}

/// <summary>Per-slab field validation (US-PAY-006 FR-1).</summary>
public sealed class TaxSlabInputValidator : AbstractValidator<TaxSlabInput>
{
    public TaxSlabInputValidator()
    {
        RuleFor(s => s.SlabFrom).GreaterThanOrEqualTo(0).WithMessage("Slab lower bound cannot be negative.");
        RuleFor(s => s.RatePercentage).InclusiveBetween(0, 100).WithMessage("Slab rate must be between 0 and 100.");
        RuleFor(s => s.OrderIndex).GreaterThanOrEqualTo(0).WithMessage("Slab order index cannot be negative.");
        RuleFor(s => s)
            .Must(s => s.SlabTo is null || s.SlabTo.Value > s.SlabFrom)
            .WithMessage("Slab upper bound must be greater than its lower bound.");
    }
}

/// <summary>Social-security parameter validation (US-PAY-006 FR-2).</summary>
public sealed class SocialSecurityInputValidator : AbstractValidator<SocialSecurityInputDto>
{
    public SocialSecurityInputValidator()
    {
        RuleFor(s => s.EmployeeRate).InclusiveBetween(0, 100).WithMessage("Employee rate must be between 0 and 100.");
        RuleFor(s => s.EmployerRate).InclusiveBetween(0, 100).WithMessage("Employer rate must be between 0 and 100.");
        RuleFor(s => s.WageCeilingAnnual)
            .GreaterThan(0).When(s => s.WageCeilingAnnual.HasValue)
            .WithMessage("Wage ceiling must be greater than 0 when supplied.");
        RuleFor(s => s.ApplicableOn).IsInEnum().WithMessage("Applicable-on is invalid.");
        RuleFor(s => s.ApplicableComponentIds)
            .NotEmpty().When(s => s.ApplicableOn == StatutoryApplicableOn.Custom)
            .WithMessage("At least one component id is required when the contribution applies on Custom components.");
    }
}
