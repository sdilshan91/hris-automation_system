using FluentValidation;
using HRM.Application.Features.CustomFields.Commands;

namespace HRM.Application.Features.CustomFields.Validators;

public sealed class CreateCustomFieldValidator : AbstractValidator<CreateCustomFieldCommand>
{
    private static readonly HashSet<string> ValidFieldTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "textarea", "number", "date", "dropdown",
        "multi_select", "checkbox", "email", "phone", "url"
    };

    private static readonly HashSet<string> ValidEntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "employee"
    };

    public CreateCustomFieldValidator()
    {
        RuleFor(x => x.EntityType)
            .NotEmpty().WithMessage("Entity type is required.")
            .MaximumLength(50).WithMessage("Entity type cannot exceed 50 characters.")
            .Must(t => ValidEntityTypes.Contains(t))
            .WithMessage("Entity type must be 'employee' (Phase 1).");

        RuleFor(x => x.FieldName)
            .NotEmpty().WithMessage("Field name is required.")
            .MaximumLength(100).WithMessage("Field name cannot exceed 100 characters.");

        RuleFor(x => x.FieldKey)
            .MaximumLength(100).WithMessage("Field key cannot exceed 100 characters.")
            .Matches(@"^[a-z0-9_]+$").When(x => !string.IsNullOrEmpty(x.FieldKey))
            .WithMessage("Field key must contain only lowercase letters, digits, and underscores.");

        RuleFor(x => x.FieldType)
            .NotEmpty().WithMessage("Field type is required.")
            .MaximumLength(30).WithMessage("Field type cannot exceed 30 characters.")
            .Must(t => ValidFieldTypes.Contains(t))
            .WithMessage($"Field type must be one of: {string.Join(", ", ValidFieldTypes)}.");

        RuleFor(x => x.Options)
            .NotEmpty().When(x => x.FieldType is "dropdown" or "multi_select")
            .WithMessage("Options are required for dropdown and multi_select field types.");
    }
}
