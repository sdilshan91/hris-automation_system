using FluentValidation;
using HRM.Application.Features.Attendance.Commands;
using HRM.Application.Features.Attendance.DTOs;

namespace HRM.Application.Features.Attendance.Validators;

/// <summary>
/// CAL-4b / US-ATT-011 AC-3: shape validation for an attendance-policy payload, shared by the
/// tenant-default and per-Location upserts (both carry the same policy payload — the only difference is
/// the SCOPE, which comes from the route).
///
/// <para>Note: <c>FteScaledOvertimeBase</c> (US-ATT-011 AC-5) has no rule here — a bool has no invalid
/// value. It is nonetheless part of the FULL-REPLACE payload and IS mapped in AttendanceSettingsService.</para>
///
/// <para>Shape only. DB-dependent checks (does the location exist / is it same-tenant / is it active) are
/// NOT here — FluentValidation has no DB access — and live in <c>AttendanceSettingsService</c>, mirroring
/// LocationService's pre-check pattern.</para>
/// </summary>
public sealed class AttendanceSettingsPolicyValidator : AbstractValidator<AttendanceSettingsDto>
{
    public AttendanceSettingsPolicyValidator()
    {
        // ── Overtime multipliers: a multiplier below 1.0 would pay overtime LESS than regular time; the
        // upper bound catches a fat-fingered 15 that would quietly inflate the payroll run.
        RuleFor(x => x.WeekdayOvertimeMultiplier)
            .InclusiveBetween(1.0m, 10m).WithMessage("Weekday overtime multiplier must be between 1.0 and 10.");

        RuleFor(x => x.WeekendOvertimeMultiplier)
            .InclusiveBetween(1.0m, 10m).WithMessage("Weekend overtime multiplier must be between 1.0 and 10.");

        RuleFor(x => x.HolidayOvertimeMultiplier)
            .InclusiveBetween(1.0m, 10m).WithMessage("Holiday overtime multiplier must be between 1.0 and 10.");

        // ── Minute / day fields are all durations: never negative.
        RuleFor(x => x.GracePeriodMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Grace period minutes cannot be negative.");

        RuleFor(x => x.StandardWorkMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Standard work minutes cannot be negative.");

        RuleFor(x => x.MinimumWorkMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Minimum work minutes cannot be negative.");

        RuleFor(x => x.AutoBreakMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Auto break minutes cannot be negative.");

        RuleFor(x => x.AutoBreakThresholdMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Auto break threshold minutes cannot be negative.");

        RuleFor(x => x.OvertimeThresholdMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Overtime threshold minutes cannot be negative.");

        RuleFor(x => x.RegularizationLookbackDays)
            .GreaterThanOrEqualTo(0).WithMessage("Regularization lookback days cannot be negative.");

        RuleFor(x => x.OvertimeMinimumThresholdMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Overtime minimum threshold minutes cannot be negative.");

        RuleFor(x => x.MaxDailyOvertimeMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Max daily overtime minutes cannot be negative.");

        RuleFor(x => x.MaxWeeklyOvertimeMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Max weekly overtime minutes cannot be negative.");

        RuleFor(x => x.AbsenteeismThresholdDays)
            .GreaterThanOrEqualTo(0m).WithMessage("Absenteeism threshold days cannot be negative.");

        // ── A full day cannot be shorter than the minimum that qualifies as one: with minimum > standard,
        // every worked day would be flagged SHORT_DAY (US-ATT-002 BR-3/BR-4 read both together).
        RuleFor(x => x.MinimumWorkMinutes)
            .LessThanOrEqualTo(x => x.StandardWorkMinutes)
            .WithMessage("Minimum work minutes cannot exceed standard work minutes.");

        // ── Geo-fence: a radius is only meaningful — and only enforced — when the fence is on.
        RuleFor(x => x.GeoFenceRadiusMeters)
            .GreaterThanOrEqualTo(1).WithMessage("Geo-fence radius must be at least 1 metre when the geo-fence is enabled.")
            .When(x => x.GeoFenceEnabled);

        RuleFor(x => x.GeoFenceLatitude!.Value)
            .InclusiveBetween(-90m, 90m).WithMessage("Geo-fence latitude must be between -90 and 90.")
            .When(x => x.GeoFenceLatitude.HasValue);

        RuleFor(x => x.GeoFenceLongitude!.Value)
            .InclusiveBetween(-180m, 180m).WithMessage("Geo-fence longitude must be between -180 and 180.")
            .When(x => x.GeoFenceLongitude.HasValue);
    }
}

/// <summary>
/// CAL-4b / US-ATT-011 AC-3: validates the TENANT-DEFAULT attendance-policy upsert payload. The service
/// re-checks DB-dependent rules; this gives the MediatR ValidationBehavior a fast, consistent 400 before
/// the handler runs.
/// </summary>
public sealed class UpsertAttendanceSettingsValidator : AbstractValidator<UpsertAttendanceSettingsCommand>
{
    public UpsertAttendanceSettingsValidator()
    {
        RuleFor(x => x.Settings).NotNull();
        RuleFor(x => x.Settings).SetValidator(new AttendanceSettingsPolicyValidator())
            .When(x => x.Settings is not null);
    }
}
