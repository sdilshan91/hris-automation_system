using FluentValidation;
using HRM.Application.Features.Onboarding.Commands;

namespace HRM.Application.Features.Onboarding.Validators;

/// <summary>
/// Validates onboarding checklist template creation input (US-ONB-001). Covers structural field
/// limits/ranges and the business rules enforceable without DB access: BR-2 (>= 1 task), BR-3
/// (due_offset_days >= 0), responsible_role in {HR, Manager, IT, Employee, Custom}. The unique-name
/// rule (AC-3/BR-1) requires the tenant DB and is enforced in the service (returns a 409), mirroring how
/// other modules check uniqueness in the service layer.
/// </summary>
public sealed class CreateChecklistTemplateValidator : AbstractValidator<CreateChecklistTemplateCommand>
{
    public CreateChecklistTemplateValidator()
    {
        RuleFor(x => x.TemplateName)
            .NotEmpty().WithMessage("Template name is required.")
            .MinimumLength(3).WithMessage("Template name must be at least 3 characters.")
            .MaximumLength(200).WithMessage("Template name cannot exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.")
            .When(x => x.Description is not null);

        // BR-2: a template must contain at least one task to be saved.
        RuleFor(x => x.Tasks)
            .NotEmpty().WithMessage("A template must contain at least one task.");

        RuleForEach(x => x.Tasks).SetValidator(new CreateChecklistTemplateTaskValidator());
    }
}

/// <summary>Validates a single task line on the create command (US-ONB-001 FR-3, BR-3).</summary>
public sealed class CreateChecklistTemplateTaskValidator : AbstractValidator<CreateChecklistTemplateTask>
{
    public CreateChecklistTemplateTaskValidator()
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

        // responsible_role in {HR, Manager, IT, Employee, Custom}.
        RuleFor(t => t.ResponsibleRole)
            .IsInEnum().WithMessage("Responsible role is invalid.");

        // BR-3: due offset must be a non-negative integer (0 = same day as joining).
        RuleFor(t => t.DueOffsetDays)
            .GreaterThanOrEqualTo(0).WithMessage("Due offset days must be zero or greater.");

        RuleFor(t => t.SortOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Sort order must be zero or greater.");
    }
}
