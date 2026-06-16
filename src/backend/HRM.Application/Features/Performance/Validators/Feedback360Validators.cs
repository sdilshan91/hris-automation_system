using FluentValidation;
using HRM.Application.Features.Performance.Commands;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Features.Performance.Validators;

/// <summary>
/// Field-shape rules for a single 360 feedback item (US-PRF-005 FR-4).
///
/// SCOPE: only the rules a command can verify in isolation — exactly one of GoalId/CompetencyKey set, the
/// rating's lower bound and the comment length ceiling. The rating's UPPER bound depends on the cycle's
/// configured scale (RatingScaleMax) and the reviewer's Pending assignment / BR-3 de-duplication depend on
/// the persisted rows, so those are enforced in <c>Feedback360Service</c> against the DB.
/// </summary>
internal sealed class Feedback360ItemValidator : AbstractValidator<Feedback360ItemInput>
{
    public Feedback360ItemValidator()
    {
        RuleFor(i => i)
            .Must(i => (i.GoalId.HasValue && i.GoalId != Guid.Empty) ^ !string.IsNullOrWhiteSpace(i.CompetencyKey))
            .WithMessage("Each item must reference exactly one of a goal or a competency key.");

        RuleFor(i => i.CompetencyKey)
            .MaximumLength(200).WithMessage("Competency key cannot exceed 200 characters.");

        RuleFor(i => i.Rating)
            .GreaterThanOrEqualTo(1).WithMessage("Rating must be at least 1.");

        RuleFor(i => i.Comment)
            .MaximumLength(2000).WithMessage("Comment cannot exceed 2000 characters.");
    }
}

/// <summary>Validates <see cref="AddReviewerCommand"/> shape (US-PRF-005 AC-1/FR-2).</summary>
public sealed class AddReviewerValidator : AbstractValidator<AddReviewerCommand>
{
    public AddReviewerValidator()
    {
        RuleFor(x => x.CycleId).NotEmpty().WithMessage("A cycle is required.");
        RuleFor(x => x.RevieweeEmployeeId).NotEmpty().WithMessage("A reviewee is required.");
        RuleFor(x => x.ReviewerEmployeeId).NotEmpty().WithMessage("A reviewer is required.");
        RuleFor(x => x.ReviewerEmployeeId)
            .NotEqual(x => x.RevieweeEmployeeId)
            .WithMessage("An employee cannot review themselves in this category.");
    }
}

/// <summary>Validates <see cref="SubmitFeedback360Command"/> shape (US-PRF-005 AC-3/FR-4).</summary>
public sealed class SubmitFeedback360Validator : AbstractValidator<SubmitFeedback360Command>
{
    public SubmitFeedback360Validator()
    {
        RuleFor(x => x.CycleId).NotEmpty().WithMessage("A cycle is required.");
        RuleFor(x => x.RevieweeEmployeeId).NotEmpty().WithMessage("A reviewee is required.");
        RuleFor(x => x.OverallComment)
            .MaximumLength(5000).WithMessage("Overall comment cannot exceed 5000 characters.");
        RuleFor(x => x.Items)
            .NotNull().WithMessage("Items are required.")
            .NotEmpty().WithMessage("At least one competency rating is required to submit feedback.");
        RuleForEach(x => x.Items).SetValidator(new Feedback360ItemValidator());
    }
}
