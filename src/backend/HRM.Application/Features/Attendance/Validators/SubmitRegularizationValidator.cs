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
/// "HH:mm", and that the COMBINED clock-in/clock-out are logically consistent — clock-in before
/// clock-out (FR-5, frame-independent since both combine identically). The future-DATE guard here is
/// coarse (date-only, UTC-calendar with a +1 day tolerance); the authoritative tenant-local future-date
/// rejection lives in the service (ISSUE-072). The reason minimum length (BR-7) is also checked here.
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

        // ISSUE-072 / BR-4 (coarse): the corrected DATE must not be in the future. This is a shape-level
        // guard only — the AUTHORITATIVE, tenant-local future-date rejection (with the "future_date" code)
        // lives in the service (AttendanceService.SubmitRegularizationAsync, using TenantClock.TodayIn).
        // We deliberately compare DATE-only against the UTC calendar day with a +1 day tolerance: the
        // previous rule combined the wall-clock "HH:mm" with the date AS UTC and compared to DateTime.UtcNow,
        // which wrongly rejected valid local-past times for tenants ahead of UTC (up to +14h). A stateless
        // validator has no tenant time zone, so the precise boundary is left to the service.
        RuleFor(x => x.Request.Date)
            .Must(d => d <= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            .WithMessage("The corrected date cannot be in the future.")
            .OverridePropertyName(nameof(SubmitRegularizationCommand.Request));
    }
}
