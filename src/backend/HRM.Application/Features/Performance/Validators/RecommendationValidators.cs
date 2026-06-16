using FluentValidation;
using HRM.Application.Features.Performance.Commands;

namespace HRM.Application.Features.Performance.Validators;

/// <summary>
/// Field-shape rules for saving a recommendation (US-PRF-010 FR-1). The BR-5 promotion-details requirement, the
/// FR-3 mandatory-justification-on-override rule, the BR-1/BR-2 submit gates and HR-only authorization (AC-5) are
/// persistence/permission concerns enforced in <c>RecommendationService</c> — encoding them here would be a false
/// guarantee (the service owns the state that decides them).
/// </summary>
public sealed class SaveRecommendationValidator : AbstractValidator<SaveRecommendationCommand>
{
    public SaveRecommendationValidator()
    {
        RuleFor(c => c.Input.EmployeeId).NotEmpty().WithMessage("An employee is required.");
        RuleFor(c => c.Input.CycleId).NotEmpty().WithMessage("A cycle is required.");
        RuleFor(c => c.Input.Justification)
            .MaximumLength(4000).WithMessage("Justification cannot exceed 4000 characters.");
        RuleFor(c => c.Input.Details.TargetGrade)
            .MaximumLength(100).WithMessage("Target grade cannot exceed 100 characters.");
        RuleFor(c => c.Input.Details.TrainingCourse)
            .MaximumLength(300).WithMessage("Training course cannot exceed 300 characters.");
        RuleFor(c => c.Input.Details.BonusPercent)
            .InclusiveBetween(0, 1000).When(c => c.Input.Details.BonusPercent.HasValue)
            .WithMessage("Bonus percentage must be between 0 and 1000.");
        RuleFor(c => c.Input.Details.IncrementPercent)
            .InclusiveBetween(0, 1000).When(c => c.Input.Details.IncrementPercent.HasValue)
            .WithMessage("Increment percentage must be between 0 and 1000.");
        RuleFor(c => c.Input.Details.BonusAmount)
            .GreaterThanOrEqualTo(0).When(c => c.Input.Details.BonusAmount.HasValue)
            .WithMessage("Bonus amount cannot be negative.");
        RuleFor(c => c.Input.Details.IncrementAmount)
            .GreaterThanOrEqualTo(0).When(c => c.Input.Details.IncrementAmount.HasValue)
            .WithMessage("Increment amount cannot be negative.");
    }
}

/// <summary>Validates the auto-generate command shape (US-PRF-010 AC-2).</summary>
public sealed class AutoGenerateRecommendationsValidator : AbstractValidator<AutoGenerateRecommendationsCommand>
{
    public AutoGenerateRecommendationsValidator()
        => RuleFor(c => c.CycleId).NotEmpty().WithMessage("A cycle is required.");
}

/// <summary>Validates the budget command shape (US-PRF-010 FR-8).</summary>
public sealed class SaveBudgetValidator : AbstractValidator<SaveBudgetCommand>
{
    public SaveBudgetValidator()
    {
        RuleFor(c => c.Input.CycleId).NotEmpty().WithMessage("A cycle is required.");
        RuleFor(c => c.Input.Name)
            .NotEmpty().WithMessage("A budget name is required.")
            .MaximumLength(200).WithMessage("Budget name cannot exceed 200 characters.");
        RuleFor(c => c.Input.AllocatedAmount)
            .GreaterThanOrEqualTo(0).WithMessage("The allocated amount cannot be negative.");
    }
}

/// <summary>Validates the rule command shape (US-PRF-010 FR-2).</summary>
public sealed class SaveRuleValidator : AbstractValidator<SaveRuleCommand>
{
    public SaveRuleValidator()
    {
        RuleFor(c => c.Input.Name)
            .NotEmpty().WithMessage("A rule name is required.")
            .MaximumLength(200).WithMessage("Rule name cannot exceed 200 characters.");
        RuleFor(c => c.Input.MinFinalScore)
            .GreaterThanOrEqualTo(0).WithMessage("The threshold score cannot be negative.");
    }
}
