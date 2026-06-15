using FluentValidation;
using HRM.Application.Features.Recruitment.Commands;

namespace HRM.Application.Features.Recruitment.Validators;

/// <summary>Validates the requested target status is a defined enum value (US-REC-001 FR-2). The legal
/// transition itself is checked in the service against the current status.</summary>
public sealed class ChangeVacancyStatusValidator : AbstractValidator<ChangeVacancyStatusCommand>
{
    public ChangeVacancyStatusValidator()
    {
        RuleFor(x => x.VacancyId).NotEmpty().WithMessage("Vacancy id is required.");
        RuleFor(x => x.Status).IsInEnum().WithMessage("Status is invalid.");
    }
}

/// <summary>Validates the bulk status-change request shape (US-REC-001 FR-6).</summary>
public sealed class BulkChangeVacancyStatusValidator : AbstractValidator<BulkChangeVacancyStatusCommand>
{
    public BulkChangeVacancyStatusValidator()
    {
        RuleFor(x => x.VacancyIds)
            .NotEmpty().WithMessage("At least one vacancy id is required.")
            .Must(ids => ids.Count <= 200).WithMessage("A bulk operation can cover at most 200 vacancies.");

        RuleFor(x => x.Status).IsInEnum().WithMessage("Status is invalid.");
    }
}
