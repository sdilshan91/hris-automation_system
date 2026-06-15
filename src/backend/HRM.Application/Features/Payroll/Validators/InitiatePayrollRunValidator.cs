using FluentValidation;
using HRM.Application.Features.Payroll.Commands;

namespace HRM.Application.Features.Payroll.Validators;

/// <summary>
/// Structural validation for initiating a payroll run (US-PAY-003 FR-1). BR-1 (one non-cancelled run per
/// period), AC-4 (already-finalized → 409), and FR-9 (idempotency) need DB context and are enforced in the
/// service.
/// </summary>
public sealed class InitiatePayrollRunValidator : AbstractValidator<InitiatePayrollRunCommand>
{
    public InitiatePayrollRunValidator()
    {
        RuleFor(x => x.PayMonth)
            .InclusiveBetween(1, 12).WithMessage("Pay month must be between 1 and 12.");

        RuleFor(x => x.PayYear)
            .InclusiveBetween(2000, 2100).WithMessage("Pay year must be a valid year.");
    }
}
