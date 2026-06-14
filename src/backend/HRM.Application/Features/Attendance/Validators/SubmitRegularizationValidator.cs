using FluentValidation;
using HRM.Application.Features.Attendance.Commands;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;

namespace HRM.Application.Features.Attendance.Validators;

/// <summary>
/// Shape-level validation for a regularization submission (US-ATT-003 §7, FR-5, BR-7). Policy and
/// state rules that need DB access (lookback FR-6/BR-2, duplicate-pending BR-3, locked payroll period
/// FR-7/BR-6, active-employee; the future-DATE BR-4 check) are enforced in the service layer.
///
/// The corrected times arrive as wall-clock "HH:mm" strings paired with <c>Date</c>; this validator
/// checks the request shape: a valid type, the conditionally-required times are present and parse as
/// "HH:mm", and that the COMBINED (date + HH:mm, treated as UTC) timestamps are logically consistent —
/// clock-in before clock-out (FR-5) and not in the future versus now (UTC). Combined times trivially
/// fall on the regularized date (we combine them with that date), so no separate same-day check is
/// needed. The reason minimum length (BR-7) is also checked here.
/// </summary>
public sealed class SubmitRegularizationValidator : AbstractValidator<SubmitRegularizationCommand>
{
    public SubmitRegularizationValidator()
    {
        RuleFor(x => x.Request.Date)
            .NotEqual(default(DateOnly)).WithMessage("A date is required.");

        RuleFor(x => x.Request.RegularizationType)
            .NotEmpty().WithMessage("Regularization type is required.")
            .Must(t => RegularizationType.All.Contains(t))
            .WithMessage("Regularization type must be one of MISSED_CLOCK_IN, MISSED_CLOCK_OUT, MISSED_BOTH.");

        // BR-7: reason mandatory, >= 10 characters (after trimming).
        RuleFor(x => x.Request.Reason)
            .NotEmpty().WithMessage("A reason is required.")
            .Must(r => (r ?? string.Empty).Trim().Length >= 10)
            .WithMessage("The reason must be at least 10 characters.");

        // §7: clock-in is required for MISSED_CLOCK_IN / MISSED_BOTH; when supplied it must be "HH:mm".
        RuleFor(x => x.Request.RequestedClockIn)
            .NotNull()
            .When(x => RegularizationType.RequiresClockIn(x.Request.RegularizationType ?? string.Empty))
            .WithMessage("A corrected clock-in time is required for this regularization type.");

        RuleFor(x => x.Request.RequestedClockIn)
            .Must(v => SubmitRegularizationRequest.TryParseTime(v, out _))
            .When(x => x.Request.RequestedClockIn is not null)
            .WithMessage("The clock-in time must be a valid 24-hour time in HH:mm format.");

        // §7: clock-out is required for MISSED_CLOCK_OUT / MISSED_BOTH; when supplied it must be "HH:mm".
        RuleFor(x => x.Request.RequestedClockOut)
            .NotNull()
            .When(x => RegularizationType.RequiresClockOut(x.Request.RegularizationType ?? string.Empty))
            .WithMessage("A corrected clock-out time is required for this regularization type.");

        RuleFor(x => x.Request.RequestedClockOut)
            .Must(v => SubmitRegularizationRequest.TryParseTime(v, out _))
            .When(x => x.Request.RequestedClockOut is not null)
            .WithMessage("The clock-out time must be a valid 24-hour time in HH:mm format.");

        // FR-5: clock-in must be before clock-out when both combine to valid instants.
        RuleFor(x => x.Request)
            .Must(r =>
            {
                var inTs = r.CombineToUtc(r.RequestedClockIn);
                var outTs = r.CombineToUtc(r.RequestedClockOut);
                return !(inTs.HasValue && outTs.HasValue) || inTs.Value < outTs.Value;
            })
            .WithMessage("The clock-in time must be before the clock-out time.")
            .OverridePropertyName(nameof(SubmitRegularizationCommand.Request));

        // BR-4 (fine-grained): the combined date+time must not be in the future versus now (UTC).
        // The service still enforces the future-DATE rejection with the exact code; this catches a
        // valid (e.g. today's) date paired with a still-future time.
        RuleFor(x => x.Request)
            .Must(r =>
            {
                var now = DateTime.UtcNow;
                var inTs = r.CombineToUtc(r.RequestedClockIn);
                var outTs = r.CombineToUtc(r.RequestedClockOut);
                return (inTs is null || inTs.Value <= now) && (outTs is null || outTs.Value <= now);
            })
            .WithMessage("The corrected time cannot be in the future.")
            .OverridePropertyName(nameof(SubmitRegularizationCommand.Request));
    }
}
