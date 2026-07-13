using FluentValidation;
using HRM.Application.Features.Payroll.Commands;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;

namespace HRM.Application.Features.Payroll.Validators;

/// <summary>
/// Structural validation for creating a salary component (US-PAY-001 FR-1). Uniqueness of the code per
/// tenant (BR-2) needs the DB and is enforced in the service. Formula syntax (BR-6) is checked here AND
/// in the service; circular references (across components) are a structure concern, validated there.
/// </summary>
public sealed class CreateSalaryComponentValidator : AbstractValidator<CreateSalaryComponentCommand>
{
    public CreateSalaryComponentValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Component name is required.")
            .MaximumLength(100).WithMessage("Component name cannot exceed 100 characters.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Component code is required.")
            .MaximumLength(50).WithMessage("Component code cannot exceed 50 characters.")
            .Matches("^[A-Za-z0-9_]+$").WithMessage("Component code may contain only letters, digits, and underscores.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Component type is invalid.");

        RuleFor(x => x.CalculationMethod)
            .IsInEnum().WithMessage("Calculation method is invalid.");

        // Formula method: expression required + must parse under the safe grammar (BR-6 syntax).
        When(x => x.CalculationMethod == CalculationMethod.Formula, () =>
        {
            RuleFor(x => x.FormulaExpression)
                .NotEmpty().WithMessage("A formula expression is required for the Formula calculation method.")
                .Must(f => SalaryFormula.Validate(f) is null)
                .WithMessage(x => $"Formula is invalid: {SalaryFormula.Validate(x.FormulaExpression)}");
        });

        // Non-formula methods: a default value is required.
        When(x => x.CalculationMethod != CalculationMethod.Formula, () =>
        {
            RuleFor(x => x.DefaultValue)
                .NotNull().WithMessage("A default value is required for non-formula calculation methods.");
        });

        When(x => x.CalculationMethod is CalculationMethod.PercentageOfBasic or CalculationMethod.PercentageOfGross
                  && x.DefaultValue.HasValue, () =>
        {
            RuleFor(x => x.DefaultValue!.Value)
                .InclusiveBetween(0, 100).WithMessage("A percentage value must be between 0 and 100.");
        });

        // BUG-061: a Fixed monetary amount cannot be negative (negative fixed pay/deduction is always invalid).
        When(x => x.CalculationMethod == CalculationMethod.Fixed && x.DefaultValue.HasValue, () =>
        {
            RuleFor(x => x.DefaultValue!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("A fixed component value cannot be negative.")
                .WithErrorCode("negative_fixed_value");
        });

        RuleFor(x => x.ProcessingOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Processing order cannot be negative.");
    }
}
