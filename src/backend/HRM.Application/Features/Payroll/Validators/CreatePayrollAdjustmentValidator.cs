using FluentValidation;
using HRM.Application.Features.Payroll.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Payroll.Validators;

/// <summary>
/// Structural validation for creating a payroll adjustment (US-PAY-007 FR-1). Business rules that need the DB
/// (BR-1 active salary assignment, BR-7 finalized-period retarget, BR-8 processing-period defer, BR-3
/// negative-net warning, Correction reference existence) are enforced in <c>PayrollAdjustmentService</c>.
/// </summary>
public sealed class CreatePayrollAdjustmentValidator : AbstractValidator<CreatePayrollAdjustmentCommand>
{
    public CreatePayrollAdjustmentValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty().WithMessage("Employee is required.");

        RuleFor(x => x.AdjustmentType)
            .NotEmpty().WithMessage("Adjustment type is required.")
            .Must(t => Enum.TryParse<AdjustmentType>(t, ignoreCase: false, out _))
            .WithMessage("Adjustment type must be one of: Bonus, Deduction, Reimbursement, Correction.");

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("Amount must be greater than zero.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MaximumLength(1000);

        RuleFor(x => x.ApplicablePayMonth)
            .InclusiveBetween(1, 12).WithMessage("Applicable pay month must be between 1 and 12.");

        RuleFor(x => x.ApplicablePayYear)
            .InclusiveBetween(2000, 2100).WithMessage("Applicable pay year must be a valid year.");

        // A Correction must reference the original slip being corrected (BR-5/FR-7).
        RuleFor(x => x.ReferencePayrollSlipId)
            .NotNull().NotEqual(Guid.Empty)
            .When(x => string.Equals(x.AdjustmentType, nameof(AdjustmentType.Correction), StringComparison.Ordinal))
            .WithMessage("A correction adjustment must reference the original payslip.");

        // Recurring window must be supplied and not before the start period (FR-5).
        When(x => x.IsRecurring, () =>
        {
            RuleFor(x => x.RecurrenceEndMonth)
                .NotNull().InclusiveBetween(1, 12)
                .WithMessage("Recurring adjustments require a valid recurrence end month (1-12).");

            RuleFor(x => x.RecurrenceEndYear)
                .NotNull().InclusiveBetween(2000, 2100)
                .WithMessage("Recurring adjustments require a valid recurrence end year.");

            RuleFor(x => x)
                .Must(x => !x.RecurrenceEndYear.HasValue || !x.RecurrenceEndMonth.HasValue
                    || x.RecurrenceEndYear.Value * 12 + x.RecurrenceEndMonth.Value
                       >= x.ApplicablePayYear * 12 + x.ApplicablePayMonth)
                .WithMessage("Recurrence end must be on or after the applicable period.");

            // A recurring Correction makes no sense (arrears references a specific slip).
            RuleFor(x => x.AdjustmentType)
                .NotEqual(nameof(AdjustmentType.Correction))
                .WithMessage("Correction adjustments cannot be recurring.");
        });
    }
}
