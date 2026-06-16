using FluentValidation;
using HRM.Application.Features.Performance.Commands;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Features.Performance.Validators;

/// <summary>
/// Field-shape rules for a single self-assessment item (US-PRF-002 FR-1/FR-3).
///
/// SCOPE: only the rules a command can verify in isolation — goal id present, achievement 0-100, and (on
/// submit) the rating present + comment ≥20 chars. The rating's UPPER bound depends on the cycle's
/// configured scale (RatingScaleMax, BR-4) and "every assigned goal rated" (BR-2) depend on DB rows, so
/// both are enforced in <c>SelfAssessmentService</c> against the persisted goal set — putting them here
/// would be a false guarantee.
/// </summary>
internal static class SelfAssessmentItemRules
{
    public const int MinCommentLength = 20; // FR-3

    public static void ApplyDraft(AbstractValidator<SelfAssessmentItemInput> v)
    {
        v.RuleFor(i => i.GoalId).NotEmpty().WithMessage("A goal id is required.");

        v.RuleFor(i => i.SelfRating!.Value)
            .GreaterThanOrEqualTo(1).WithMessage("Self-rating must be at least 1.")
            .When(i => i.SelfRating.HasValue);

        v.RuleFor(i => i.AchievementPercentage!.Value)
            .InclusiveBetween(0, 100).WithMessage("Achievement percentage must be between 0 and 100.")
            .When(i => i.AchievementPercentage.HasValue);

        v.RuleFor(i => i.Comment)
            .MaximumLength(4000).WithMessage("Comment cannot exceed 4000 characters.");
    }

    public static void ApplySubmit(AbstractValidator<SelfAssessmentItemInput> v)
    {
        ApplyDraft(v);

        v.RuleFor(i => i.SelfRating)
            .NotNull().WithMessage("All goals must be rated before submitting.");

        v.RuleFor(i => i.AchievementPercentage)
            .NotNull().WithMessage("Each goal must have an achievement percentage before submitting.");

        v.RuleFor(i => i.Comment)
            .NotEmpty().WithMessage($"Each goal comment must be at least {MinCommentLength} characters.")
            .MinimumLength(MinCommentLength).WithMessage($"Each goal comment must be at least {MinCommentLength} characters.");
    }
}

internal sealed class SelfAssessmentDraftItemValidator : AbstractValidator<SelfAssessmentItemInput>
{
    public SelfAssessmentDraftItemValidator() => SelfAssessmentItemRules.ApplyDraft(this);
}

internal sealed class SelfAssessmentSubmitItemValidator : AbstractValidator<SelfAssessmentItemInput>
{
    public SelfAssessmentSubmitItemValidator() => SelfAssessmentItemRules.ApplySubmit(this);
}

/// <summary>Validates <see cref="SaveSelfAssessmentDraftCommand"/> shape (US-PRF-002 FR-6).</summary>
public sealed class SaveSelfAssessmentDraftValidator : AbstractValidator<SaveSelfAssessmentDraftCommand>
{
    public SaveSelfAssessmentDraftValidator()
    {
        RuleFor(x => x.CycleId).NotEmpty().WithMessage("A cycle is required.");
        RuleFor(x => x.Items).NotNull().WithMessage("Items are required.");
        RuleForEach(x => x.Items).SetValidator(new SelfAssessmentDraftItemValidator());
    }
}

/// <summary>Validates <see cref="SubmitSelfAssessmentCommand"/> shape (US-PRF-002 AC-2/BR-2/FR-3).</summary>
public sealed class SubmitSelfAssessmentValidator : AbstractValidator<SubmitSelfAssessmentCommand>
{
    public SubmitSelfAssessmentValidator()
    {
        RuleFor(x => x.CycleId).NotEmpty().WithMessage("A cycle is required.");
        RuleFor(x => x.Items)
            .NotNull().WithMessage("Items are required.")
            .NotEmpty().WithMessage("At least one goal rating is required to submit.");
        RuleForEach(x => x.Items).SetValidator(new SelfAssessmentSubmitItemValidator());
    }
}
