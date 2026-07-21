using FluentValidation;
using HRM.Application.Features.Onboarding.Commands;

namespace HRM.Application.Features.Onboarding.Validators;

/// <summary>
/// US-ONB-006 §7 input validation for recording an exit interview. Structural / data-shape rules only:
/// interview_mode value, interview_date not in the future, rating ranges (1-5), free_text &lt;= 2000,
/// additional_comments &lt;= 5000. The per-question TYPE check (a rating answer only on a rating question,
/// etc.) and the BR-1 duplicate / template-membership checks need the tenant template and are enforced in
/// the service (which returns the user-facing validation error).
/// </summary>
public sealed class RecordExitInterviewValidator : AbstractValidator<RecordExitInterviewCommand>
{
    private static readonly string[] ValidModes = ["hr_conducted", "self_service"];

    public RecordExitInterviewValidator()
    {
        RuleFor(x => x.Input.OffboardingId)
            .NotEmpty().WithMessage("Offboarding is required.");

        RuleFor(x => x.Input.InterviewMode)
            .NotEmpty().WithMessage("Interview mode is required.")
            .Must(m => ValidModes.Contains(m?.Trim().ToLowerInvariant()))
            .WithMessage("Interview mode must be 'hr_conducted' or 'self_service'.");

        RuleFor(x => x.Input.InterviewDate)
            .Must(d => d <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Interview date cannot be in the future.");

        RuleFor(x => x.Input.OverallExperienceRating)
            .InclusiveBetween(1, 5).WithMessage("Overall experience rating must be between 1 and 5.")
            .When(x => x.Input.OverallExperienceRating is not null);

        RuleFor(x => x.Input.AdditionalComments)
            .MaximumLength(5000).WithMessage("Additional comments cannot exceed 5000 characters.")
            .When(x => x.Input.AdditionalComments is not null);

        RuleFor(x => x.Input.Responses)
            .NotNull().WithMessage("Responses are required.");

        RuleForEach(x => x.Input.Responses).ChildRules(r =>
        {
            r.RuleFor(x => x.QuestionId).NotEmpty().WithMessage("Each response must reference a question.");

            r.RuleFor(x => x.Rating)
                .InclusiveBetween(1, 5).WithMessage("A rating answer must be between 1 and 5.")
                .When(x => x.Rating is not null);

            r.RuleFor(x => x.SelectedOption)
                .MaximumLength(200).WithMessage("A selected option cannot exceed 200 characters.")
                .When(x => x.SelectedOption is not null);

            r.RuleFor(x => x.FreeText)
                .MaximumLength(2000).WithMessage("A free-text answer cannot exceed 2000 characters.")
                .When(x => x.FreeText is not null);
        });
    }
}
