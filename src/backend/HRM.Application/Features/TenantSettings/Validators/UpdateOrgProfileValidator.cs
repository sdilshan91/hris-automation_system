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
    }
}
