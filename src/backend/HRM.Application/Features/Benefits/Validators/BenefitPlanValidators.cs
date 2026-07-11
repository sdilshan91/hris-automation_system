using FluentValidation;
using HRM.Application.Features.Benefits.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Benefits.Validators;

/// <summary>Validates <see cref="CreateBenefitPlanCommand"/> (US-TRN-002 AC-1).</summary>
public sealed class CreateBenefitPlanValidator : AbstractValidator<CreateBenefitPlanCommand>
{
    private static readonly HashSet<string> s_validTypes =
        new(Enum.GetNames<BenefitType>(), StringComparer.OrdinalIgnoreCase);

    public CreateBenefitPlanValidator()
    {
        RuleFor(x => x.Request.Name)
            .NotEmpty().WithMessage("Plan name is required.")
            .MaximumLength(200).WithMessage("Plan name cannot exceed 200 characters.");

        RuleFor(x => x.Request.Type)
            .NotEmpty().WithMessage("Benefit type is required.")
            .Must(t => s_validTypes.Contains(t))
            .WithMessage("Benefit type must be one of: Health, Dental, Vision, Life, Retirement, Disability, Other.");

        RuleFor(x => x.Request.Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.");

        RuleFor(x => x.Request.CoverageDetails)
            .MaximumLength(4000).WithMessage("Coverage details cannot exceed 4000 characters.");

        RuleFor(x => x.Request.EmployerCost)
            .GreaterThanOrEqualTo(0).When(x => x.Request.EmployerCost.HasValue)
            .WithMessage("Employer cost must be zero or a positive amount.");

        RuleFor(x => x.Request.EmployeeCost)
            .GreaterThanOrEqualTo(0).When(x => x.Request.EmployeeCost.HasValue)
            .WithMessage("Employee cost must be zero or a positive amount.");

        RuleFor(x => x.Request)
            .Must(r => r.EffectiveTo is null || r.EffectiveTo >= r.EffectiveFrom)
            .WithMessage("Effective-to date must be on or after the effective-from date.");

        RuleFor(x => x.Request)
            .Must(r => r.EnrollmentOpensAt is null || r.EnrollmentClosesAt is null
                       || r.EnrollmentClosesAt >= r.EnrollmentOpensAt)
            .WithMessage("Enrollment-closes date must be on or after the enrollment-opens date.");
    }
}

/// <summary>Validates <see cref="UpdateBenefitPlanCommand"/> (US-TRN-002 AC-4).</summary>
public sealed class UpdateBenefitPlanValidator : AbstractValidator<UpdateBenefitPlanCommand>
{
    private static readonly HashSet<string> s_validTypes =
        new(Enum.GetNames<BenefitType>(), StringComparer.OrdinalIgnoreCase);

    public UpdateBenefitPlanValidator()
    {
        RuleFor(x => x.Request.Name)
            .NotEmpty().WithMessage("Plan name is required.")
            .MaximumLength(200).WithMessage("Plan name cannot exceed 200 characters.");

        RuleFor(x => x.Request.Type)
            .NotEmpty().WithMessage("Benefit type is required.")
            .Must(t => s_validTypes.Contains(t))
            .WithMessage("Benefit type must be one of: Health, Dental, Vision, Life, Retirement, Disability, Other.");

        RuleFor(x => x.Request.Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.");

        RuleFor(x => x.Request.CoverageDetails)
            .MaximumLength(4000).WithMessage("Coverage details cannot exceed 4000 characters.");

        RuleFor(x => x.Request.EmployerCost)
            .GreaterThanOrEqualTo(0).When(x => x.Request.EmployerCost.HasValue)
            .WithMessage("Employer cost must be zero or a positive amount.");

        RuleFor(x => x.Request.EmployeeCost)
            .GreaterThanOrEqualTo(0).When(x => x.Request.EmployeeCost.HasValue)
            .WithMessage("Employee cost must be zero or a positive amount.");

        RuleFor(x => x.Request)
            .Must(r => r.EffectiveTo is null || r.EffectiveTo >= r.EffectiveFrom)
            .WithMessage("Effective-to date must be on or after the effective-from date.");

        RuleFor(x => x.Request)
            .Must(r => r.EnrollmentOpensAt is null || r.EnrollmentClosesAt is null
                       || r.EnrollmentClosesAt >= r.EnrollmentOpensAt)
            .WithMessage("Enrollment-closes date must be on or after the enrollment-opens date.");
    }
}
