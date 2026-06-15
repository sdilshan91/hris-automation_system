using FluentValidation;
using HRM.Application.Features.Recruitment.Commands;

namespace HRM.Application.Features.Recruitment.Validators;

/// <summary>
/// Validates converting an applicant to an employee (US-REC-010). Structural + stateless rules only:
/// the applicant, the structured job title + department, the employment type, and the date of joining are
/// required (FR-3 — the HR Officer completes the pre-filled form). The stateful rules (applicant exists +
/// is Hired + has an accepted offer FR-1, not already converted FR-10/BR-2, email uniqueness, plan limit
/// BR-3) are enforced in <c>ApplicantConversionService</c>.
/// </summary>
public sealed class ConvertApplicantToEmployeeValidator : AbstractValidator<ConvertApplicantToEmployeeCommand>
{
    public const int MaxEmployeeNoLength = 50;

    public ConvertApplicantToEmployeeValidator()
    {
        RuleFor(x => x.ApplicantId)
            .NotEmpty().WithMessage("Applicant is required.");

        RuleFor(x => x.JobTitleId)
            .NotEmpty().WithMessage("A job title is required.");

        RuleFor(x => x.DepartmentId)
            .NotEmpty().WithMessage("A department is required.");

        RuleFor(x => x.EmploymentType)
            .IsInEnum().WithMessage("A valid employment type is required.");

        RuleFor(x => x.DateOfJoining)
            .NotEmpty().WithMessage("A date of joining is required.");

        RuleFor(x => x.EmployeeNo)
            .MaximumLength(MaxEmployeeNoLength)
            .When(x => !string.IsNullOrWhiteSpace(x.EmployeeNo))
            .WithMessage($"Employee number must be at most {MaxEmployeeNoLength} characters.");
    }
}
