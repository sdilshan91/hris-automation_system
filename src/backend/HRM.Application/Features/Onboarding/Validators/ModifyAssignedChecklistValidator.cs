using FluentValidation;
using HRM.Application.Features.Onboarding.Commands;

namespace HRM.Application.Features.Onboarding.Validators;

/// <summary>
/// Validates checklist-modification input (US-ONB-002 AC-4). The checklist instance id is required and the
/// modification must do something (at least one add or change). Ad-hoc task field limits are validated;
/// per-task changes require a task id. BR-3 (mandatory not removable) and existence are enforced in the
/// service (returns 404/409) since they need the persisted checklist.
/// </summary>
public sealed class ModifyAssignedChecklistValidator : AbstractValidator<ModifyAssignedChecklistCommand>
{
    public ModifyAssignedChecklistValidator()
    {
        RuleFor(x => x.ChecklistInstanceId)
            .NotEmpty().WithMessage("Checklist instance id is required.");

        RuleFor(x => x)
            .Must(x => x.AddTasks.Count > 0 || x.TaskChanges.Count > 0)
            .WithMessage("At least one task to add or change is required.");

        RuleForEach(x => x.AddTasks).SetValidator(new AssignChecklistAdHocTaskValidator());

        RuleForEach(x => x.TaskChanges).ChildRules(c =>
        {
            c.RuleFor(t => t.TaskInstanceId)
                .NotEmpty().WithMessage("Task instance id is required.");
        });
    }
}
