using FluentValidation;
using HRM.Application.Features.Onboarding.Commands;

namespace HRM.Application.Features.Onboarding.Validators;

/// <summary>
/// Validates checklist-assignment input (US-ONB-002 §7). Covers structural rules enforceable without DB
/// access: employee/template ids required, valid assignment mode, and ad-hoc task field limits. Existence
/// of the employee/template (and BR-1 active, BR-2 single-active) is enforced in the service (returns
/// 404/409), mirroring how the other modules check DB-dependent rules in the service layer.
/// </summary>
public sealed class AssignChecklistValidator : AbstractValidator<AssignChecklistCommand>
{
    public AssignChecklistValidator()
    {
        RuleFor(x => x.EmployeeId)
            .NotEmpty().WithMessage("Employee id is required.");

        RuleFor(x => x.TemplateId)
            .NotEmpty().WithMessage("Template id is required.");

        RuleFor(x => x.Mode)
            .IsInEnum().WithMessage("Assignment mode is invalid.");

        RuleForEach(x => x.AdditionalTasks).SetValidator(new AssignChecklistAdHocTaskValidator());
    }
}

/// <summary>Validates a single ad-hoc task line on the assign command (US-ONB-002 FR-5).</summary>
public sealed class AssignChecklistAdHocTaskValidator : AbstractValidator<AssignChecklistAdHocTask>
{
    public AssignChecklistAdHocTaskValidator()
    {
        RuleFor(t => t.Title)
            .NotEmpty().WithMessage("Task title is required.")
            .MinimumLength(3).WithMessage("Task title must be at least 3 characters.")
            .MaximumLength(200).WithMessage("Task title cannot exceed 200 characters.");

        RuleFor(t => t.Description)
            .MaximumLength(1000).WithMessage("Task description cannot exceed 1000 characters.")
            .When(t => t.Description is not null);

        RuleFor(t => t.Category)
            .MaximumLength(100).WithMessage("Task category cannot exceed 100 characters.")
            .When(t => t.Category is not null);

        RuleFor(t => t.ResponsibleRole)
            .IsInEnum().WithMessage("Responsible role is invalid.");

        RuleFor(t => t.DueOffsetDays)
            .GreaterThanOrEqualTo(0).WithMessage("Due offset days must be zero or greater.");

        RuleFor(t => t.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Sort order must be zero or greater.");
    }
}
