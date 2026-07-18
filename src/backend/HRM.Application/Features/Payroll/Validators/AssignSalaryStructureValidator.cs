using FluentValidation;
using HRM.Application.Features.Payroll.Commands;

namespace HRM.Application.Features.Payroll.Validators;

/// <summary>
/// Structural validation for assigning a salary structure to an employee (US-PAY-002 FR-1). FR-7 (inactive
/// structure → 400) and FR-6 (sum-of-components == CTC within ±1) need DB / cross-component context and are
/// enforced in the service.
/// </summary>
public sealed class AssignSalaryStructureValidator : AbstractValidator<AssignSalaryStructureCommand>
{
    public AssignSalaryStructureValidator()
    {
        RuleFor(x => x.EmployeeId)
            .NotEmpty().WithMessage("An employee reference is required.");

        RuleFor(x => x.SalaryStructureId)
            .NotEmpty().WithMessage("A salary structure reference is required.");

        RuleFor(x => x.EffectiveFrom)
            .NotEqual(default(DateOnly)).WithMessage("An effective-from date is required.");

        RuleFor(x => x.AnnualCtc)
            .GreaterThan(0).WithMessage("Annual CTC must be greater than zero.")
            // ISSUE-152: enforce the numeric(18,2) money contract — reject (do NOT silently round) a declared
            // CTC carrying more than 2 decimal places (e.g. 600000.555). ignoreTrailingZeros so 50000.50 is fine.
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
                .WithMessage("Annual CTC cannot have more than 2 decimal places.")
                .WithErrorCode("invalid_ctc_scale");

        RuleForEach(x => x.Overrides).ChildRules(o =>
        {
            o.RuleFor(c => c.SalaryComponentId)
                .NotEmpty().WithMessage("Each override must reference a salary component.");
            o.RuleFor(c => c.AnnualAmount)
                .GreaterThanOrEqualTo(0).WithMessage("An override amount cannot be negative.");
        });
    }
}
