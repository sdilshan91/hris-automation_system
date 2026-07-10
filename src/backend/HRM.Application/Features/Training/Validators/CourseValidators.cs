using FluentValidation;
using HRM.Application.Features.Training.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Training.Validators;

/// <summary>Validates <see cref="CreateCourseCommand"/> (US-TRN-001 AC-1).</summary>
public sealed class CreateCourseValidator : AbstractValidator<CreateCourseCommand>
{
    private static readonly HashSet<string> s_validModes =
        new(Enum.GetNames<TrainingMode>(), StringComparer.OrdinalIgnoreCase);

    public CreateCourseValidator()
    {
        RuleFor(x => x.Request.Title)
            .NotEmpty().WithMessage("Course title is required.")
            .MaximumLength(200).WithMessage("Course title cannot exceed 200 characters.");

        RuleFor(x => x.Request.Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.");

        RuleFor(x => x.Request.Mode)
            .NotEmpty().WithMessage("Mode is required.")
            .Must(m => s_validModes.Contains(m)).WithMessage("Mode must be one of: InPerson, Online, Hybrid.");

        RuleFor(x => x.Request.Capacity)
            .GreaterThanOrEqualTo(0).When(x => x.Request.Capacity.HasValue)
            .WithMessage("Capacity must be zero or a positive number.");

        RuleFor(x => x.Request.DurationHours)
            .GreaterThan(0).When(x => x.Request.DurationHours.HasValue)
            .WithMessage("Duration hours must be a positive number.");

        RuleFor(x => x.Request)
            .Must(r => r.StartDate is null || r.EndDate is null || r.EndDate >= r.StartDate)
            .WithMessage("End date must be on or after the start date.");
    }
}

/// <summary>Validates <see cref="UpdateCourseCommand"/> (US-TRN-001 FR-3).</summary>
public sealed class UpdateCourseValidator : AbstractValidator<UpdateCourseCommand>
{
    private static readonly HashSet<string> s_validModes =
        new(Enum.GetNames<TrainingMode>(), StringComparer.OrdinalIgnoreCase);

    public UpdateCourseValidator()
    {
        RuleFor(x => x.Request.Title)
            .NotEmpty().WithMessage("Course title is required.")
            .MaximumLength(200).WithMessage("Course title cannot exceed 200 characters.");

        RuleFor(x => x.Request.Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.");

        RuleFor(x => x.Request.Mode)
            .NotEmpty().WithMessage("Mode is required.")
            .Must(m => s_validModes.Contains(m)).WithMessage("Mode must be one of: InPerson, Online, Hybrid.");

        RuleFor(x => x.Request.Capacity)
            .GreaterThanOrEqualTo(0).When(x => x.Request.Capacity.HasValue)
            .WithMessage("Capacity must be zero or a positive number.");

        RuleFor(x => x.Request.DurationHours)
            .GreaterThan(0).When(x => x.Request.DurationHours.HasValue)
            .WithMessage("Duration hours must be a positive number.");

        RuleFor(x => x.Request)
            .Must(r => r.StartDate is null || r.EndDate is null || r.EndDate >= r.StartDate)
            .WithMessage("End date must be on or after the start date.");
    }
}
