using FluentValidation;
using HRM.Application.Features.Performance.Commands;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Features.Performance.Validators;

/// <summary>
/// Field-shape rules for one PIP objective (US-PRF-008 FR-1). The ≥30-day duration (BR-3), the one-active-PIP
/// rule (BR-2) and HR-only authorization (BR-1) are persistence/permission concerns enforced in
/// <c>PipService</c> — putting them here would be a false guarantee.
/// </summary>
internal static class PipObjectiveRules
{
    public static void Apply(AbstractValidator<PipObjectiveInput> v)
    {
        v.RuleFor(o => o.Title)
            .NotEmpty().WithMessage("An objective title is required.")
            .MaximumLength(200).WithMessage("Objective title cannot exceed 200 characters.");

        v.RuleFor(o => o.Description)
            .MaximumLength(2000).WithMessage("Objective description cannot exceed 2000 characters.");

        v.RuleFor(o => o.SuccessCriteria)
            .NotEmpty().WithMessage("Success criteria are required.")
            .MaximumLength(2000).WithMessage("Success criteria cannot exceed 2000 characters.");
    }
}

internal sealed class PipObjectiveValidator : AbstractValidator<PipObjectiveInput>
{
    public PipObjectiveValidator() => PipObjectiveRules.Apply(this);
}

/// <summary>Validates the <see cref="CreatePipCommand"/> shape (US-PRF-008 AC-1/FR-1).</summary>
public sealed class CreatePipValidator : AbstractValidator<CreatePipCommand>
{
    public CreatePipValidator()
    {
        RuleFor(c => c.Input.EmployeeId).NotEmpty().WithMessage("An employee is required.");

        RuleFor(c => c.Input.Reason)
            .NotEmpty().WithMessage("A PIP reason is required.")
            .MaximumLength(4000).WithMessage("The PIP reason cannot exceed 4000 characters.");

        RuleFor(c => c.Input.EndDate)
            .GreaterThan(c => c.Input.StartDate)
            .WithMessage("The PIP end date must be after the start date.");

        RuleFor(c => c.Input.Objectives)
            .NotEmpty().WithMessage("At least one improvement objective is required.");

        RuleForEach(c => c.Input.Objectives).SetValidator(new PipObjectiveValidator());
    }
}

/// <summary>Validates the <see cref="RecordPipCheckpointCommand"/> shape (US-PRF-008 AC-3/FR-4).</summary>
public sealed class RecordPipCheckpointValidator : AbstractValidator<RecordPipCheckpointCommand>
{
    public RecordPipCheckpointValidator()
    {
        RuleFor(c => c.Input.PipId).NotEmpty().WithMessage("A PIP id is required.");
        RuleFor(c => c.Input.EvidenceNotes)
            .NotEmpty().WithMessage("Evidence notes are required.")
            .MaximumLength(4000).WithMessage("Evidence notes cannot exceed 4000 characters.");
    }
}

/// <summary>Validates the <see cref="SetPipOutcomeCommand"/> shape (US-PRF-008 AC-4/FR-6).</summary>
public sealed class SetPipOutcomeValidator : AbstractValidator<SetPipOutcomeCommand>
{
    public SetPipOutcomeValidator()
    {
        RuleFor(c => c.Input.PipId).NotEmpty().WithMessage("A PIP id is required.");
        RuleFor(c => c.Input.Notes).MaximumLength(4000).WithMessage("Notes cannot exceed 4000 characters.");

        // FR-6: an extension needs a new end date (the ≥30-day / after-current check is in the service).
        RuleFor(c => c.Input.NewEndDate)
            .NotNull().WithMessage("A new end date is required to extend a PIP.")
            .When(c => c.Input.Outcome == PipOutcomeKind.Extended);

        RuleForEach(c => c.Input.NewObjectives)
            .SetValidator(new PipObjectiveValidator())
            .When(c => c.Input.NewObjectives != null);
    }
}

/// <summary>Validates the <see cref="ConfirmPipEscalationCommand"/> shape (US-PRF-008 AC-5/BR-6).</summary>
public sealed class ConfirmPipEscalationValidator : AbstractValidator<ConfirmPipEscalationCommand>
{
    public ConfirmPipEscalationValidator()
    {
        RuleFor(c => c.Input.PipId).NotEmpty().WithMessage("A PIP id is required.");
        RuleFor(c => c.Input.Notes).MaximumLength(4000).WithMessage("Escalation notes cannot exceed 4000 characters.");
    }
}
