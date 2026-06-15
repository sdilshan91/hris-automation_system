using FluentValidation;
using HRM.Application.Features.Payroll.Commands;
using HRM.Domain.Payroll;

namespace HRM.Application.Features.Payroll.Validators;

/// <summary>
/// Structural validation for creating a salary structure (US-PAY-001 FR-2/FR-3). FR-5 (at least one
/// earning before active), BR-2 unique code, and BR-6 circular references need DB / cross-component
/// context and are enforced in the service. Override-formula syntax is checked here too (fail fast).
/// </summary>
public sealed class CreateSalaryStructureValidator : AbstractValidator<CreateSalaryStructureCommand>
{
    public CreateSalaryStructureValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Structure name is required.")
            .MaximumLength(100).WithMessage("Structure name cannot exceed 100 characters.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Structure code is required.")
            .MaximumLength(50).WithMessage("Structure code cannot exceed 50 characters.")
            .Matches("^[A-Za-z0-9_]+$").WithMessage("Structure code may contain only letters, digits, and underscores.");

        RuleFor(x => x.EffectiveFrom)
            .NotEqual(default(DateOnly)).WithMessage("An effective-from date is required.");

        RuleForEach(x => x.Components).ChildRules(component =>
        {
            component.RuleFor(c => c.SalaryComponentId)
                .NotEmpty().WithMessage("A component reference is required for each link.");

            component.RuleFor(c => c.ProcessingOrder)
                .GreaterThanOrEqualTo(0).WithMessage("Processing order cannot be negative.");

            component.RuleFor(c => c.OverrideFormula)
                .Must(f => SalaryFormula.Validate(f) is null)
                .WithMessage(c => $"Override formula is invalid: {SalaryFormula.Validate(c.OverrideFormula)}")
                .When(c => !string.IsNullOrWhiteSpace(c.OverrideFormula));
        });
    }
}
