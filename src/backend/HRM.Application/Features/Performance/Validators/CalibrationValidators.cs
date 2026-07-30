using FluentValidation;
using HRM.Application.Features.Performance.Commands;

namespace HRM.Application.Features.Performance.Validators;

/// <summary>
/// Validates <see cref="ApplyCalibrationCommand"/> (US-PRF-011 §3). The reason is MANDATORY (this is
/// compensation-adjacent data — every rating change must be justified). Score-range + review-exists rules are
/// DB-dependent and enforced in the service.
/// </summary>
public sealed class ApplyCalibrationCommandValidator : AbstractValidator<ApplyCalibrationCommand>
{
    public ApplyCalibrationCommandValidator()
    {
        RuleFor(x => x.Input.CycleId).NotNull().WithMessage("A cycle id is required.");
        RuleFor(x => x.Input.EmployeeId).NotEmpty().WithMessage("An employee id is required.");

        RuleFor(x => x.Input.Reason)
            .NotEmpty().WithMessage("A calibration reason is required.")
            .MaximumLength(2000).WithMessage("The calibration reason cannot exceed 2000 characters.");

        RuleFor(x => x.Input.CalibratedScore)
            .GreaterThanOrEqualTo(0).WithMessage("The calibrated score cannot be negative.");
    }
}
