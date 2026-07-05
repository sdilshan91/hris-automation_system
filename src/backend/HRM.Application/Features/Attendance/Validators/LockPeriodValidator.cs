using FluentValidation;
using HRM.Application.Features.Attendance.Commands;

namespace HRM.Application.Features.Attendance.Validators;

/// <summary>
/// Shape validation for locking an attendance period (US-ATT-009 AC-4/FR-3). Guards against an empty/missing
/// body (ISSUE-088): an uninitialised <see cref="System.DateOnly"/> defaults to 0001-01-01, which would
/// otherwise create a garbage lock. Requires both bounds to be real calendar dates and the range to be
/// ordered. Overlap/duplicate detection needs the DB and stays in the service.
/// </summary>
public sealed class LockPeriodValidator : AbstractValidator<LockPeriodCommand>
{
    // A sane floor — attendance periods are modern calendar dates, never the DateOnly default (0001-01-01).
    private static readonly DateOnly MinReasonableDate = new(2000, 1, 1);

    public LockPeriodValidator()
    {
        RuleFor(x => x.PeriodStart)
            .Must(d => d >= MinReasonableDate)
            .WithMessage("A valid period start date is required.");

        RuleFor(x => x.PeriodEnd)
            .Must(d => d >= MinReasonableDate)
            .WithMessage("A valid period end date is required.");

        RuleFor(x => x.PeriodEnd)
            .GreaterThanOrEqualTo(x => x.PeriodStart)
            .When(x => x.PeriodStart >= MinReasonableDate && x.PeriodEnd >= MinReasonableDate)
            .WithMessage("Period end must be on or after period start.");
    }
}
