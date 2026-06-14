using FluentValidation;
using HRM.Application.Features.Recruitment.Commands;

namespace HRM.Application.Features.Recruitment.Validators;

/// <summary>Validates vacancy update input (US-REC-001 AC-3). Mirrors the create field rules.</summary>
public sealed class UpdateVacancyValidator : AbstractValidator<UpdateVacancyCommand>
{
    public UpdateVacancyValidator()
    {
        RuleFor(x => x.VacancyId)
            .NotEmpty().WithMessage("Vacancy id is required.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Vacancy title is required.")
            .MaximumLength(200).WithMessage("Vacancy title cannot exceed 200 characters.");

        RuleFor(x => x.Headcount)
            .GreaterThanOrEqualTo(1).WithMessage("Headcount must be at least 1.");

        RuleFor(x => x.EmploymentType)
            .IsInEnum().WithMessage("Employment type is invalid.");

        RuleFor(x => x.Description)
            .MaximumLength(20000).WithMessage("Description cannot exceed 20000 characters.");

        RuleFor(x => x.Qualifications)
            .MaximumLength(20000).WithMessage("Qualifications cannot exceed 20000 characters.");

        RuleFor(x => x.SalaryCurrency)
            .Length(3).WithMessage("Salary currency must be a 3-letter ISO-4217 code.")
            .When(x => !string.IsNullOrWhiteSpace(x.SalaryCurrency));

        RuleFor(x => x.SalaryMin)
            .GreaterThanOrEqualTo(0).WithMessage("Salary minimum cannot be negative.")
            .When(x => x.SalaryMin.HasValue);

        RuleFor(x => x.SalaryMax)
            .GreaterThanOrEqualTo(0).WithMessage("Salary maximum cannot be negative.")
            .When(x => x.SalaryMax.HasValue);

        RuleFor(x => x)
            .Must(x => x.SalaryMax >= x.SalaryMin)
            .WithMessage("Salary maximum must be greater than or equal to salary minimum.")
            .When(x => x.SalaryMin.HasValue && x.SalaryMax.HasValue);
    }
}
