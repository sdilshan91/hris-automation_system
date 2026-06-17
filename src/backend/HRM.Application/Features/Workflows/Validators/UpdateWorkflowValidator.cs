using FluentValidation;
using HRM.Application.Features.Workflows.Commands;

namespace HRM.Application.Features.Workflows.Validators;

/// <summary>US-ADM-007 AC-3/FR-5: validate a workflow-update request (name, ≥1 valid step).</summary>
public sealed class UpdateWorkflowValidator : AbstractValidator<UpdateWorkflowCommand>
{
    public UpdateWorkflowValidator()
    {
        RuleFor(x => x.LineageId)
            .NotEmpty().WithMessage("A workflow id is required.");

        RuleFor(x => x.Request.Name)
            .NotEmpty().WithMessage("Workflow name is required.")
            .MaximumLength(150).WithMessage("Workflow name cannot exceed 150 characters.");

        RuleFor(x => x.Request.Steps)
            .NotNull().WithMessage("A workflow must have at least one step.")
            .Must(steps => steps is { Count: > 0 }).WithMessage("A workflow must have at least one step.");

        RuleForEach(x => x.Request.Steps).SetValidator(new WorkflowStepRequestValidator());
    }
}
