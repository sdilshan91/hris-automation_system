using FluentValidation;
using HRM.Application.Features.Performance.Commands;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Features.Performance.Validators;

/// <summary>
/// Field-shape rules for a single manager-review item (US-PRF-003 FR-2/FR-3).
///
/// SCOPE: only the rules a command can verify in isolation — goal id present, comment length ceiling. The
/// rating's UPPER bound depends on the cycle's configured scale (RatingScaleMax, FR-2), "every assigned
/// goal rated" (AC-3) and the comment ≥20-char floor on submit (FR-3) all depend on the persisted goal set
/// and must return the exact unrated-goal list (AC-3), so they are enforced in <c>ManagerReviewService</c>
/// against the DB rows — putting them here would be a false guarantee and would lose the goal list.
/// </summary>
internal static class ManagerReviewItemRules
{
    public static void Apply(AbstractValidator<ManagerReviewItemInput> v)
    {
        v.RuleFor(i => i.GoalId).NotEmpty().WithMessage("A goal id is required.");

        v.RuleFor(i => i.ManagerRating!.Value)
            .GreaterThanOrEqualTo(1).WithMessage("Manager rating must be at least 1.")
            .When(i => i.ManagerRating.HasValue);

        v.RuleFor(i => i.ManagerComment)
            .MaximumLength(5000).WithMessage("Manager comment cannot exceed 5000 characters.");
    }
}

internal sealed class ManagerReviewItemValidator : AbstractValidator<ManagerReviewItemInput>
{
    public ManagerReviewItemValidator() => ManagerReviewItemRules.Apply(this);
}

/// <summary>Validates <see cref="SaveManagerReviewDraftCommand"/> shape (US-PRF-003).</summary>
public sealed class SaveManagerReviewDraftValidator : AbstractValidator<SaveManagerReviewDraftCommand>
{
    public SaveManagerReviewDraftValidator()
    {
        RuleFor(x => x.CycleId).NotEmpty().WithMessage("A cycle is required.");
        RuleFor(x => x.EmployeeId).NotEmpty().WithMessage("An employee is required.");
        RuleFor(x => x.SummaryComment)
            .MaximumLength(5000).WithMessage("Summary comment cannot exceed 5000 characters.");
        RuleFor(x => x.Items).NotNull().WithMessage("Items are required.");
        RuleForEach(x => x.Items).SetValidator(new ManagerReviewItemValidator());
    }
}

/// <summary>Validates <see cref="SubmitManagerReviewCommand"/> shape (US-PRF-003 AC-2/AC-3/FR-3).</summary>
public sealed class SubmitManagerReviewValidator : AbstractValidator<SubmitManagerReviewCommand>
{
    public SubmitManagerReviewValidator()
    {
        RuleFor(x => x.CycleId).NotEmpty().WithMessage("A cycle is required.");
        RuleFor(x => x.EmployeeId).NotEmpty().WithMessage("An employee is required.");
        RuleFor(x => x.SummaryComment)
            .MaximumLength(5000).WithMessage("Summary comment cannot exceed 5000 characters.");
        RuleFor(x => x.Items)
            .NotNull().WithMessage("Items are required.")
            .NotEmpty().WithMessage("At least one goal rating is required to submit.");
        RuleForEach(x => x.Items).SetValidator(new ManagerReviewItemValidator());
    }
}
