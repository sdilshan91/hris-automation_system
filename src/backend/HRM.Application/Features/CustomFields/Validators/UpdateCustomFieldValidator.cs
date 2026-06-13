using FluentValidation;
using HRM.Application.Features.CustomFields.Commands;

namespace HRM.Application.Features.CustomFields.Validators;

public sealed class UpdateCustomFieldValidator : AbstractValidator<UpdateCustomFieldCommand>
{
    public UpdateCustomFieldValidator()
    {
        RuleFor(x => x.CustomFieldId)
            .NotEmpty().WithMessage("Custom field ID is required.");

        RuleFor(x => x.FieldName)
            .NotEmpty().WithMessage("Field name is required.")
            .MaximumLength(100).WithMessage("Field name cannot exceed 100 characters.");
    }
}
