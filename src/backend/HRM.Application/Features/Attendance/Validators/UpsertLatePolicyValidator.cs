using FluentValidation;
using HRM.Application.Features.Attendance.Commands;

namespace HRM.Application.Features.Attendance.Validators;

/// <summary>
/// US-ATT-008 FR-4: validates the late-policy upsert payload. The service re-checks defensively; this
/// gives the MediatR ValidationBehavior a fast, consistent 400 before the handler runs.
/// </summary>
public sealed class UpsertLatePolicyValidator : AbstractValidator<UpsertLatePolicyCommand>
{
    private static readonly string[] ValidPeriods = { "MONTHLY", "QUARTERLY" };

    public UpsertLatePolicyValidator()
    {
        RuleFor(x => x.Policy).NotNull();

        RuleFor(x => x.Policy.ThresholdCount)
            .GreaterThanOrEqualTo(1).WithMessage("Threshold count must be at least 1.");

        RuleFor(x => x.Policy.DeductionDays)
            .InclusiveBetween(0m, 31m).WithMessage("Deduction days must be between 0 and 31.");

        RuleFor(x => x.Policy.ChronicThreshold)
            .GreaterThanOrEqualTo(1).WithMessage("Chronic threshold must be at least 1.");

        RuleFor(x => x.Policy.Period)
            .Must(p => p != null && ValidPeriods.Contains(p.Trim().ToUpperInvariant()))
            .WithMessage("Period must be MONTHLY or QUARTERLY.");
    }
}
