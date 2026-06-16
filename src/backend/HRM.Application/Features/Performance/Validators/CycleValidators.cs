using FluentValidation;
using HRM.Application.Features.Performance.Commands;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Performance.Validators;

/// <summary>
/// Shared phase-set rules (US-PRF-004 FR-2/BR-3): non-empty, the three core phases present, each phase
/// start &lt; end, all phases within the cycle window, sequential + non-overlapping when ordered by start.
/// All of this is verifiable from the command fields alone (the whole phase set is in the request), so it
/// lives here rather than in the service. The service additionally enforces the DB-dependent rules
/// (BR-4 concurrent same-type, BR-2 delete-only, BR-5 scale-lock, valid status transitions).
/// </summary>
internal static class CyclePhaseRules
{
    public static void Apply<T>(
        AbstractValidator<T> v,
        Func<T, IReadOnlyList<CyclePhaseInput>?> phasesSelector,
        Func<T, DateTime> startSelector,
        Func<T, DateTime> endSelector)
    {
        v.RuleFor(x => x).Custom((cmd, ctx) =>
        {
            var phases = phasesSelector(cmd);
            var cycleStart = startSelector(cmd);
            var cycleEnd = endSelector(cmd);

            if (cycleStart >= cycleEnd)
                ctx.AddFailure("StartDate", "The cycle start date must be before its end date.");

            if (phases is null || phases.Count == 0)
            {
                ctx.AddFailure("Phases", "At least the goal-setting, self-assessment, and manager-review phases are required.");
                return;
            }

            // FR-1: the three core phases must be present.
            foreach (var required in new[] { CyclePhaseType.GoalSetting, CyclePhaseType.SelfAssessment, CyclePhaseType.ManagerReview })
            {
                if (phases.All(p => p.PhaseType != required))
                    ctx.AddFailure("Phases", $"The {required} phase is required.");
            }

            // No duplicate phase types.
            if (phases.Select(p => p.PhaseType).Distinct().Count() != phases.Count)
                ctx.AddFailure("Phases", "Each phase type may appear at most once in a cycle.");

            foreach (var p in phases)
            {
                if (p.StartDate >= p.EndDate)
                    ctx.AddFailure("Phases", $"The {p.PhaseType} phase start date must be before its end date.");

                // BR-3: phases must be within the cycle's overall window.
                if (p.StartDate < cycleStart || p.EndDate > cycleEnd)
                    ctx.AddFailure("Phases", $"The {p.PhaseType} phase must fall within the cycle window.");
            }

            // FR-2: sequential + non-overlapping when ordered by start date.
            var ordered = phases.OrderBy(p => p.StartDate).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].StartDate < ordered[i - 1].EndDate)
                    ctx.AddFailure("Phases",
                        $"Phases must be sequential and non-overlapping: {ordered[i - 1].PhaseType} overlaps {ordered[i].PhaseType}.");
            }
        });
    }
}

/// <summary>Validates <see cref="CreateCycleCommand"/> (US-PRF-004 AC-1/AC-2).</summary>
public sealed class CreateCycleCommandValidator : AbstractValidator<CreateCycleCommand>
{
    public CreateCycleCommandValidator()
    {
        RuleFor(x => x.Input.Name)
            .NotEmpty().WithMessage("Cycle name is required.")
            .MaximumLength(200).WithMessage("Cycle name cannot exceed 200 characters.");

        RuleFor(x => x.Input.Type).IsInEnum().WithMessage("A valid cycle type is required.");

        RuleFor(x => x.Input.RatingScaleMax)
            .InclusiveBetween(2, 10).WithMessage("The rating scale maximum must be between 2 and 10.");

        RuleFor(x => x.Input.SelfWeightPercent)
            .InclusiveBetween(0, 100).WithMessage("The self-weight percentage must be between 0 and 100.");

        RuleFor(x => x.Input.Scope).NotNull().WithMessage("A participant scope is required.");

        RuleFor(x => x.Input.Scope)
            .Must(s => s.ScopeType != ParticipantScopeType.Departments ||
                       (s.DepartmentIds is not null && s.DepartmentIds.Count > 0))
            .WithMessage("At least one department is required for a department-scoped cycle.")
            .When(x => x.Input.Scope is not null);

        RuleFor(x => x.Input.Scope)
            .Must(s => s.ScopeType != ParticipantScopeType.CustomList ||
                       (s.EmployeeIds is not null && s.EmployeeIds.Count > 0))
            .WithMessage("At least one employee is required for a custom-list cycle.")
            .When(x => x.Input.Scope is not null);

        CyclePhaseRules.Apply(this, x => x.Input.Phases, x => x.Input.StartDate, x => x.Input.EndDate);
    }
}

/// <summary>Validates <see cref="UpdateCycleCommand"/> (US-PRF-004 AC-5).</summary>
public sealed class UpdateCycleCommandValidator : AbstractValidator<UpdateCycleCommand>
{
    public UpdateCycleCommandValidator()
    {
        RuleFor(x => x.CycleId).NotEmpty();

        RuleFor(x => x.Input.Name)
            .NotEmpty().WithMessage("Cycle name is required.")
            .MaximumLength(200).WithMessage("Cycle name cannot exceed 200 characters.");

        RuleFor(x => x.Input.RatingScaleMax)
            .InclusiveBetween(2, 10).WithMessage("The rating scale maximum must be between 2 and 10.")
            .When(x => x.Input.RatingScaleMax.HasValue);

        RuleFor(x => x.Input.SelfWeightPercent)
            .InclusiveBetween(0, 100).WithMessage("The self-weight percentage must be between 0 and 100.")
            .When(x => x.Input.SelfWeightPercent.HasValue);

        CyclePhaseRules.Apply(this, x => x.Input.Phases, x => x.Input.StartDate, x => x.Input.EndDate);
    }
}

/// <summary>Validates <see cref="CloneCycleCommand"/> (US-PRF-004 FR-8).</summary>
public sealed class CloneCycleCommandValidator : AbstractValidator<CloneCycleCommand>
{
    public CloneCycleCommandValidator()
    {
        RuleFor(x => x.Input.SourceCycleId).NotEmpty();

        RuleFor(x => x.Input.Name)
            .NotEmpty().WithMessage("Cycle name is required.")
            .MaximumLength(200).WithMessage("Cycle name cannot exceed 200 characters.");

        RuleFor(x => x.Input)
            .Must(i => i.StartDate < i.EndDate)
            .WithMessage("The cycle start date must be before its end date.");
    }
}

/// <summary>Validates <see cref="TransitionCycleStatusCommand"/> (US-PRF-004 FR-7/BR-6).</summary>
public sealed class TransitionCycleStatusCommandValidator : AbstractValidator<TransitionCycleStatusCommand>
{
    public TransitionCycleStatusCommandValidator()
    {
        RuleFor(x => x.CycleId).NotEmpty();
        RuleFor(x => x.TargetStatus).IsInEnum().WithMessage("A valid target status is required.");

        // BR-6: cancellation requires a reason. (Other transitions ignore the reason field.)
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A cancellation reason is required.")
            .MaximumLength(1000).WithMessage("The cancellation reason cannot exceed 1000 characters.")
            .When(x => x.TargetStatus == AppraisalCycleStatus.Cancelled);
    }
}
