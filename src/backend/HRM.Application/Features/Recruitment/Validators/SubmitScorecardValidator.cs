using FluentValidation;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Domain.Recruitment;

namespace HRM.Application.Features.Recruitment.Validators;

/// <summary>
/// Validates a scorecard submission (US-REC-006 FR-1/BR-3). Structural + stateless rules:
/// <list type="bullet">
/// <item>Interview id required.</item>
/// <item>Overall recommendation is a valid enum value (BR-3, mandatory).</item>
/// <item>≥1 criterion rating; each key from the default set with a 1-5 score; no duplicate keys (FR-1/BR-2).</item>
/// <item>Optional comment / notes length caps.</item>
/// </list>
/// The stateful rules (the submitter is the assigned interviewer — BR-1; the lock period — BR-4) are
/// enforced in <c>ScorecardService</c>, because they require a DB lookup.
/// </summary>
public sealed class SubmitScorecardValidator : AbstractValidator<SubmitScorecardCommand>
{
    public const int MaxCommentLength = 2000;
    public const int MaxGeneralNotesLength = 4000;

    public SubmitScorecardValidator()
    {
        RuleFor(x => x.InterviewId)
            .NotEmpty().WithMessage("Interview is required.");

        RuleFor(x => x.OverallRecommendation)
            .IsInEnum().WithMessage("A valid overall recommendation is required.");

        RuleFor(x => x.Ratings)
            .NotEmpty().WithMessage("At least one criterion rating is required.")
            .Must(r => r is null || r.Select(x => x.CriterionKey).Distinct().Count() == r.Count)
            .WithMessage("The same criterion cannot be rated more than once.");

        RuleForEach(x => x.Ratings).ChildRules(rating =>
        {
            rating.RuleFor(r => r.CriterionKey)
                .Must(ScorecardCriteria.IsValidKey)
                .WithMessage("Each rating must reference a known evaluation criterion.");

            rating.RuleFor(r => r.Score)
                .InclusiveBetween(ScorecardCriteria.MinScore, ScorecardCriteria.MaxScore)
                .WithMessage($"Each rating must be between {ScorecardCriteria.MinScore} and {ScorecardCriteria.MaxScore}.");

            rating.RuleFor(r => r.Comment)
                .MaximumLength(MaxCommentLength).When(r => !string.IsNullOrWhiteSpace(r.Comment));
        });

        RuleFor(x => x.GeneralNotes)
            .MaximumLength(MaxGeneralNotesLength).When(x => !string.IsNullOrWhiteSpace(x.GeneralNotes));
    }
}
