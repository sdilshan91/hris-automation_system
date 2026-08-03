using FluentValidation;
using HRM.Application.Features.Recruitment.Commands;

namespace HRM.Application.Features.Recruitment.Validators;

/// <summary>
/// Validates generating an offer (US-REC-007 FR-1/BR-6). Structural + stateless rules:
/// <list type="bullet">
/// <item>Applicant + position + currency required.</item>
/// <item>Salary &gt; 0.</item>
/// <item>Expiry date, WHEN supplied, must be in the future (BR-6 — the service defaults a null expiry to
/// generation date + 7 days, so a null is valid here).</item>
/// <item>Field length caps; probation non-negative when supplied.</item>
/// </list>
/// The stateful rule (the applicant exists in the tenant) is enforced in <c>OfferService</c>.
/// </summary>
public sealed class GenerateOfferValidator : AbstractValidator<GenerateOfferCommand>
{
    public const int MaxPositionLength = 200;
    public const int MaxCurrencyLength = 3;
    public const int MaxBenefitsLength = 4000;
    public const int MaxCustomClausesLength = 8000;
    public const int MaxProbationMonths = 24;

    public GenerateOfferValidator()
    {
        RuleFor(x => x.ApplicantId)
            .NotEmpty().WithMessage("Applicant is required.");

        RuleFor(x => x.OfferedPosition)
            .NotEmpty().WithMessage("The offered position is required.")
            .MaximumLength(MaxPositionLength);

        RuleFor(x => x.SalaryAmount)
            .GreaterThan(0).WithMessage("The salary amount must be greater than zero.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("A currency is required.")
            .MaximumLength(MaxCurrencyLength).WithMessage("Currency must be a 3-letter ISO code.");

        RuleFor(x => x.SalaryFrequency)
            .IsInEnum().WithMessage("A valid salary frequency is required.");

        // BR-6: expiry is mandatory, but the service defaults a null to +7 days. So validate only that, if
        // supplied, it is in the future. The mandatory default is the service's job.
        RuleFor(x => x.ExpiryDate)
            .Must(d => d!.Value > DateOnly.FromDateTime(DateTime.UtcNow))
            .When(x => x.ExpiryDate.HasValue)
            .WithMessage("The offer expiry date must be in the future.");

        // BUG-067 / FR-1 / TC-REC-007-09 step 4: expiry must not precede the start date. The auto-expire job
        // fires at expiry+1d and flips the offer to Expired, so an expiry BEFORE the start date produces an
        // offer that lapses before the candidate could ever begin. Only meaningful when an expiry is supplied
        // — a null expiry is defaulted by the service to generation date + 7 days (BR-6), which is a separate
        // rule and is deliberately not cross-checked here.
        RuleFor(x => x.ExpiryDate)
            .Must((cmd, d) => d!.Value >= cmd.StartDate)
            .When(x => x.ExpiryDate.HasValue)
            .WithMessage("The offer expiry date cannot be earlier than the start date.");

        RuleFor(x => x.ProbationMonths)
            .InclusiveBetween(0, MaxProbationMonths)
            .When(x => x.ProbationMonths.HasValue)
            .WithMessage($"Probation must be between 0 and {MaxProbationMonths} months.");

        RuleFor(x => x.BenefitsSummary)
            .MaximumLength(MaxBenefitsLength).When(x => !string.IsNullOrWhiteSpace(x.BenefitsSummary));

        RuleFor(x => x.CustomClauses)
            .MaximumLength(MaxCustomClausesLength).When(x => !string.IsNullOrWhiteSpace(x.CustomClauses));
    }
}
