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

            // TAX-2: configurable exemptions (income-tax only). Per-item structural checks mirror the
            // service-layer ValidateExemptions so invalid input is caught in the MediatR pipeline too.
            When(x => x.Exemptions is not null, () =>
                RuleForEach(x => x.Exemptions!).SetValidator(new ExemptionInputValidator()));
        });

        When(x => x.RuleType != StatutoryRuleType.IncomeTax, () =>
        {
            RuleFor(x => x.SocialSecurity)
                .NotNull().WithMessage("This rule type requires social-security parameters.");

            RuleFor(x => x.TaxSlabs)
                .Empty().WithMessage("Only an income-tax rule may carry tax slabs.");

            RuleFor(x => x.Exemptions)
                .Must(e => e is null || e.Count == 0)
                .WithMessage("Only an income-tax rule may carry tax exemptions.");

            When(x => x.SocialSecurity is not null, () =>
                RuleFor(x => x.SocialSecurity!).SetValidator(new SocialSecurityInputValidator()));
        });
    }
}

/// <summary>
/// BUG-072: monetary columns are Postgres numeric(18,2) — a value above the precision max overflows
/// (22003) and 500s instead of returning a validation error. Cap monetary inputs at that maximum.
/// </summary>
internal static class StatutoryLimits
{
    public const decimal MaxMonetary = 9_999_999_999_999_999.99m; // numeric(18,2) maximum
    public const string MaxMonetaryMessage = "Amount exceeds the maximum allowed (9,999,999,999,999,999.99).";
}

/// <summary>Per-slab field validation (US-PAY-006 FR-1).</summary>
public sealed class TaxSlabInputValidator : AbstractValidator<TaxSlabInput>
{
    public TaxSlabInputValidator()
    {
        RuleFor(s => s.SlabFrom).GreaterThanOrEqualTo(0).WithMessage("Slab lower bound cannot be negative.")
            .LessThanOrEqualTo(StatutoryLimits.MaxMonetary).WithMessage(StatutoryLimits.MaxMonetaryMessage);
        RuleFor(s => s.RatePercentage)
            .InclusiveBetween(0, 100).WithMessage("Slab rate must be between 0 and 100.")
            // ISSUE-169: the column is Postgres numeric(5,2) — an over-precise rate (e.g. 12.345) was silently
            // ROUNDED to 12.35 on insert and echoed back as a different number with no error. Reject it instead
            // (same money/precision contract as ISSUE-152 on AnnualCtc). ignoreTrailingZeros so 12.50 is fine.
            .PrecisionScale(5, 2, ignoreTrailingZeros: true)
                .WithMessage("Slab rate cannot have more than 2 decimal places.")
                .WithErrorCode("invalid_rate_scale");
        RuleFor(s => s.OrderIndex).GreaterThanOrEqualTo(0).WithMessage("Slab order index cannot be negative.");
        RuleFor(s => s.SlabTo!.Value)
            .LessThanOrEqualTo(StatutoryLimits.MaxMonetary).WithMessage(StatutoryLimits.MaxMonetaryMessage)
            .When(s => s.SlabTo.HasValue);
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
        // ISSUE-501: the rate columns are Postgres numeric(5,2) — a rate carrying more than 2 decimal
        // places (e.g. 12.345) was silently rounded on insert, so the API returned success with a
        // DIFFERENT contribution rate than the caller sent. Reject it instead (same idiom as the
        // ISSUE-152 numeric(18,2) money contract). ignoreTrailingZeros so 12.30 and 12 stay valid.
        RuleFor(s => s.EmployeeRate).InclusiveBetween(0, 100).WithMessage("Employee rate must be between 0 and 100.")
            .PrecisionScale(5, 2, ignoreTrailingZeros: true)
                .WithMessage("Employee rate cannot have more than 2 decimal places.")
                .WithErrorCode("invalid_rate_scale");
        RuleFor(s => s.EmployerRate).InclusiveBetween(0, 100).WithMessage("Employer rate must be between 0 and 100.")
            .PrecisionScale(5, 2, ignoreTrailingZeros: true)
                .WithMessage("Employer rate cannot have more than 2 decimal places.")
                .WithErrorCode("invalid_rate_scale");
        RuleFor(s => s.WageCeilingAnnual)
            .GreaterThan(0).When(s => s.WageCeilingAnnual.HasValue)
            .WithMessage("Wage ceiling must be greater than 0 when supplied.");
        RuleFor(s => s.WageCeilingAnnual!.Value)
            .LessThanOrEqualTo(StatutoryLimits.MaxMonetary).When(s => s.WageCeilingAnnual.HasValue)
            .WithMessage(StatutoryLimits.MaxMonetaryMessage);
        RuleFor(s => s.ApplicableOn).IsInEnum().WithMessage("Applicable-on is invalid.");
        RuleFor(s => s.ApplicableComponentIds)
            .NotEmpty().When(s => s.ApplicableOn == StatutoryApplicableOn.Custom)
            .WithMessage("At least one component id is required when the contribution applies on Custom components.");
    }
}

/// <summary>TAX-2: per-exemption field validation (shared by the create + update validators).</summary>
public sealed class ExemptionInputValidator : AbstractValidator<ExemptionInput>
{
    public ExemptionInputValidator()
    {
        RuleFor(e => e.Name)
            .NotEmpty().WithMessage("Each tax exemption requires a name.")
            .MaximumLength(100).WithMessage("Exemption name cannot exceed 100 characters.");
        RuleFor(e => e.CalculationType).IsInEnum().WithMessage("Exemption calculation type is invalid.");
        RuleFor(e => e.OrderIndex).GreaterThanOrEqualTo(0).WithMessage("Exemption order index cannot be negative.");
        RuleFor(e => e.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Exemption value cannot be negative.")
            .LessThanOrEqualTo(StatutoryLimits.MaxMonetary).WithMessage(StatutoryLimits.MaxMonetaryMessage);
        // Percentage types: value is a percent → 0..100.
        RuleFor(e => e.Value)
            .LessThanOrEqualTo(100)
            .When(e => e.CalculationType is ExemptionCalculationType.PercentOfGross or ExemptionCalculationType.PercentOfComponent)
            .WithMessage("A percentage exemption value cannot exceed 100.");
        // PercentOfComponent requires a target component.
        RuleFor(e => e.ComponentId)
            .NotNull()
            .When(e => e.CalculationType == ExemptionCalculationType.PercentOfComponent)
            .WithMessage("A component id is required for a percent-of-component exemption.");
        RuleFor(e => e.MaxAmount!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Exemption maximum amount cannot be negative.")
            .LessThanOrEqualTo(StatutoryLimits.MaxMonetary).WithMessage(StatutoryLimits.MaxMonetaryMessage)
            .When(e => e.MaxAmount.HasValue);
    }
}
