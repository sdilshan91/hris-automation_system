using FluentValidation;
using HRM.Application.Features.JobTitles.Commands;

namespace HRM.Application.Features.JobTitles.Validators;

public sealed class UpdateJobTitleValidator : AbstractValidator<UpdateJobTitleCommand>
{
    public UpdateJobTitleValidator()
    {
        RuleFor(x => x.JobTitleId)
            .NotEmpty().WithMessage("Job title ID is required.");

        RuleFor(x => x.TitleName)
            .NotEmpty().WithMessage("Job title name is required.")
            .MaximumLength(150).WithMessage("Job title name cannot exceed 150 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters.");
    }
}
