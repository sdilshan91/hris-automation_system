using System.Linq.Expressions;
using FluentValidation;
using HRM.Application.Features.Performance.Commands;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Features.Performance.Validators;

/// <summary>
/// Shared per-goal field rules for create/update (US-PRF-001 FR-2 field limits + BR-3 weight increment).
///
/// SCOPE: these are the rules a single command can verify in isolation — title/description length, the
/// 1-100 weight range, and the 5%-increment rule (BR-3). The CROSS-goal rules that depend on other rows
/// in the DB — weights summing to exactly 100% (FR-3/AC-3) and the 1-10 goals-per-employee-per-cycle
/// count (BR-2) — CANNOT be checked from a single command's fields, so they are enforced in
/// <c>GoalService</c> against the persisted set. Putting them here would be a false guarantee.
/// </summary>
internal static class GoalFieldRules
{
    public static void Apply<T>(
        AbstractValidator<T> v,
        Expression<Func<T, string>> title,
        Expression<Func<T, string?>> description,
        Expression<Func<T, int>> weight,
        Expression<Func<T, string>> targetValue,
        Expression<Func<T, string>> measurementUnit)
    {
        v.RuleFor(title)
            .NotEmpty().WithMessage("Goal title is required.")
            .MaximumLength(200).WithMessage("Goal title cannot exceed 200 characters.");

        v.RuleFor(description)
            .MaximumLength(2000).WithMessage("Goal description cannot exceed 2000 characters.");

        v.RuleFor(weight)
            .InclusiveBetween(1, 100).WithMessage("Goal weight must be between 1% and 100%.")
            .Must(w => w % 5 == 0).WithMessage("Goal weight must be in increments of 5%.");

        v.RuleFor(targetValue)
            .NotEmpty().WithMessage("Target value is required.")
            .MaximumLength(200).WithMessage("Target value cannot exceed 200 characters.");

        v.RuleFor(measurementUnit)
            .NotEmpty().WithMessage("Measurement unit is required.")
            .MaximumLength(50).WithMessage("Measurement unit cannot exceed 50 characters.");
    }
}

/// <summary>Validates <see cref="CreateGoalCommand"/> field shape (US-PRF-001 FR-2/BR-3).</summary>
public sealed class CreateGoalValidator : AbstractValidator<CreateGoalCommand>
{
    public CreateGoalValidator()
    {
        RuleFor(x => x.CycleId).NotEmpty().WithMessage("A cycle is required.");
        RuleFor(x => x.EmployeeId).NotEmpty().WithMessage("An employee is required.");
        RuleFor(x => x.Category).IsInEnum().WithMessage("Goal category is invalid.");
        GoalFieldRules.Apply(
            this,
            x => x.Title, x => x.Description,
            x => x.Weight, x => x.TargetValue, x => x.MeasurementUnit);
    }
}

/// <summary>Validates <see cref="UpdateGoalCommand"/> field shape (US-PRF-001 FR-2/BR-3).</summary>
public sealed class UpdateGoalValidator : AbstractValidator<UpdateGoalCommand>
{
    public UpdateGoalValidator()
    {
        RuleFor(x => x.GoalId).NotEmpty().WithMessage("A goal id is required.");
        RuleFor(x => x.Category).IsInEnum().WithMessage("Goal category is invalid.");
        GoalFieldRules.Apply(
            this,
            x => x.Title, x => x.Description,
            x => x.Weight, x => x.TargetValue, x => x.MeasurementUnit);
    }
}

/// <summary>
/// Validates one <see cref="SaveGoalItem"/> in a bulk full-replace save (BUG-243). Applies the same
/// per-goal field shape as create/update (FR-2 field limits + BR-3 5%-increment weight rule). The
/// cross-goal rules (≤10 count, ≤100% total) depend on the whole set + DB and are enforced in GoalService.
/// </summary>
public sealed class SaveGoalItemValidator : AbstractValidator<SaveGoalItem>
{
    public SaveGoalItemValidator()
    {
        RuleFor(x => x.Category).IsInEnum().WithMessage("Goal category is invalid.");
        GoalFieldRules.Apply(
            this,
            x => x.Title, x => x.Description,
            x => x.Weight, x => x.TargetValue, x => x.MeasurementUnit);
    }
}

/// <summary>Validates <see cref="SaveGoalsCommand"/> — the bulk full-replace save (BUG-243).</summary>
public sealed class SaveGoalsValidator : AbstractValidator<SaveGoalsCommand>
{
    public SaveGoalsValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty().WithMessage("An employee is required.");
        RuleFor(x => x.CycleId).NotEmpty().WithMessage("A cycle is required.");
        RuleFor(x => x.Goals).NotNull().WithMessage("A goals list is required.");
        RuleForEach(x => x.Goals).SetValidator(new SaveGoalItemValidator());
    }
}
