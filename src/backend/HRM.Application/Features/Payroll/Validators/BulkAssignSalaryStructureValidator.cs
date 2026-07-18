using FluentValidation;
using HRM.Application.Features.Payroll.Commands;

namespace HRM.Application.Features.Payroll.Validators;

/// <summary>
/// Structural validation for bulk-assigning a salary structure (US-PAY-002 AC-4/FR-5). FR-7 (inactive
/// structure) and FR-6 (CTC/sum tolerance) are enforced per-employee in the service; per-employee failures
/// are returned in the result rather than failing the whole batch.
/// </summary>
public sealed class BulkAssignSalaryStructureValidator : AbstractValidator<BulkAssignSalaryStructureCommand>
{
    public BulkAssignSalaryStructureValidator()
    {
        RuleFor(x => x.SalaryStructureId)
            .NotEmpty().WithMessage("A salary structure reference is required.");

        RuleFor(x => x.EffectiveFrom)
            .NotEqual(default(DateOnly)).WithMessage("An effective-from date is required.");

        RuleFor(x => x.Employees)
            .NotEmpty().WithMessage("At least one employee is required for bulk assignment.");

        RuleForEach(x => x.Employees).ChildRules(e =>
        {
            e.RuleFor(c => c.EmployeeId)
                .NotEmpty().WithMessage("Each entry must reference an employee.");
            e.RuleFor(c => c.AnnualCtc)
                .GreaterThan(0).WithMessage("Annual CTC must be greater than zero for every employee.")
                // ISSUE-152: same numeric(18,2) money contract as the single-assign path.
                .PrecisionScale(18, 2, ignoreTrailingZeros: true)
                    .WithMessage("Annual CTC cannot have more than 2 decimal places.")
                    .WithErrorCode("invalid_ctc_scale");
        });
    }
}
