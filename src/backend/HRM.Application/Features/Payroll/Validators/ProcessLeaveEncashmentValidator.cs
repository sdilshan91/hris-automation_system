using FluentValidation;
using HRM.Application.Features.Payroll.Commands;

namespace HRM.Application.Features.Payroll.Validators;

/// <summary>
/// Structural validation for a leave-encashment trigger (US-PAY-010 AC-3/FR-5). Employee existence, active
/// salary (BR-1), and leave-type encashment eligibility (BR-6) need DB context and are enforced in the service.
/// </summary>
public sealed class ProcessLeaveEncashmentValidator : AbstractValidator<ProcessLeaveEncashmentCommand>
{
    public ProcessLeaveEncashmentValidator()
    {
        RuleFor(x => x.EmployeeId).NotEmpty().WithMessage("Employee is required.");

        RuleFor(x => x.EligibleDays)
            .GreaterThan(0m).WithMessage("Eligible days must be greater than zero.");

        RuleFor(x => x.PayMonth)
            .InclusiveBetween(1, 12).WithMessage("Pay month must be between 1 and 12.");

        RuleFor(x => x.PayYear)
            .InclusiveBetween(2000, 2100).WithMessage("Pay year must be a valid year.");
    }
}
