using FluentValidation;
using HRM.Application.Features.JobTitles.Commands;

namespace HRM.Application.Features.JobTitles.Validators;

public sealed class DeactivateJobTitleValidator : AbstractValidator<DeactivateJobTitleCommand>
{
    public DeactivateJobTitleValidator()
    {
        RuleFor(x => x.JobTitleId)
            .NotEmpty().WithMessage("Job title ID is required.");
    }
}
