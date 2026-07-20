using FluentValidation;
using HRM.Application.Features.TenantSettings.Commands;

namespace HRM.Application.Features.TenantSettings.Validators;

/// <summary>US-ADM-006 AC-1: validate the organization-profile update.</summary>
public sealed class UpdateOrgProfileValidator : AbstractValidator<UpdateOrgProfileCommand>
{
    public UpdateOrgProfileValidator()
    {
        RuleFor(x => x.Request.Name)
            .NotEmpty().WithMessage("Company name is required.")
            .MaximumLength(200).WithMessage("Company name cannot exceed 200 characters.");

        RuleFor(x => x.Request.LegalName)
            .MaximumLength(200).WithMessage("Legal name cannot exceed 200 characters.");

        RuleFor(x => x.Request.RegistrationNumber)
            .MaximumLength(100).WithMessage("Registration number cannot exceed 100 characters.");

        RuleFor(x => x.Request.Address)
            .MaximumLength(1000).WithMessage("Address cannot exceed 1000 characters.");

        RuleFor(x => x.Request.Industry)
            .MaximumLength(100).WithMessage("Industry cannot exceed 100 characters.");

        RuleFor(x => x.Request.CompanySize)
            .MaximumLength(50).WithMessage("Company size cannot exceed 50 characters.");

        RuleFor(x => x.Request.FiscalYearStartMonth)
            .InclusiveBetween(1, 12).WithMessage("Fiscal year start month must be between 1 and 12.");

        // ISSUE-304 (US-CHR-009 BR-6): the probation period drives the probation-end reminder and, transitively,
        // probation-gated leave eligibility. 0 or a negative would end probation on/before the joining date;
        // the upper bound is a sanity rail (~5 years) rather than a legal limit.
        RuleFor(x => x.Request.ProbationPeriodDays)
            .InclusiveBetween(1, 1825)
            .WithMessage("Probation period must be between 1 and 1825 days.");

        // DF-20/ISSUE-044 (US-LV-010 FR-7): the leave-cancellation notice window. 0 = an approved leave is
        // cancellable strictly before start (today's behaviour); the upper bound is a sanity rail.
        RuleFor(x => x.Request.LeaveCancellationWindowDays)
            .InclusiveBetween(0, 90)
            .WithMessage("Leave cancellation window must be between 0 and 90 days.");

        // Multi-country tax foundation: optional default ISO country code (alpha-2/alpha-3) — the fallback tax country.
        RuleFor(x => x.Request.DefaultCountryCode)
            .MaximumLength(5).WithMessage("Default country code cannot exceed 5 characters.")
            .Matches("^[A-Za-z]{2,5}$").WithMessage("Default country code must be a 2-5 letter ISO code (e.g. 'LK', 'IN').")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.DefaultCountryCode));
    }
}
