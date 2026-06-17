using FluentValidation;
using HRM.Application.Features.Workflows.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Workflows.Validators;

/// <summary>US-ADM-007 AC-2/FR-5: validate a workflow-create request (name, entity type, ≥1 valid step).</summary>
public sealed class CreateWorkflowValidator : AbstractValidator<CreateWorkflowCommand>
{
    public CreateWorkflowValidator()
    {
        RuleFor(x => x.Request.Name)
            .NotEmpty().WithMessage("Workflow name is required.")
            .MaximumLength(150).WithMessage("Workflow name cannot exceed 150 characters.");

        RuleFor(x => x.Request.EntityType)
            .Must(BeAValidEntityType)
            .WithMessage(x => $"Invalid entity type '{x.Request.EntityType}'. Valid: {string.Join(", ", Enum.GetNames<WorkflowEntityType>())}.");

        // FR-5: at least one step.
        RuleFor(x => x.Request.Steps)
            .NotNull().WithMessage("A workflow must have at least one step.")
            .Must(steps => steps is { Count: > 0 }).WithMessage("A workflow must have at least one step.");

        RuleForEach(x => x.Request.Steps).SetValidator(new WorkflowStepRequestValidator());
    }

    private static bool BeAValidEntityType(string value)
        => Enum.TryParse<WorkflowEntityType>(value, ignoreCase: true, out _);
}
