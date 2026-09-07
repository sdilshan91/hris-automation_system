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
/// <remarks>
/// BUG-522: the three overtime multipliers are <c>numeric(3,2)</c> columns
/// (<c>AttendanceSettingsConfiguration.cs:91,96,101</c>), so 9.99 is the largest value Postgres can
/// store. The bound was 10m, which meant exactly <c>10.00</c> passed validation and then failed on
/// INSERT with a 22003 numeric overflow — surfacing as a 500 rather than a 400.
///
/// <para>The bound is lowered to match the column rather than widening the column, because a 10x
/// overtime premium is not a real payroll configuration and a migration would be a larger change for
/// no user-facing gain. If a tenant ever genuinely needs it, widen the column AND this bound together
/// — they must not drift apart again.</para>
/// </remarks>
public sealed class AttendanceSettingsPolicyValidator : AbstractValidator<AttendanceSettingsDto>
{
    public AttendanceSettingsPolicyValidator()
    {
        // ── Overtime multipliers: a multiplier below 1.0 would pay overtime LESS than regular time; the
        // upper bound catches a fat-fingered 15 that would quietly inflate the payroll run.
        RuleFor(x => x.WeekdayOvertimeMultiplier)
            .InclusiveBetween(1.0m, 9.99m).WithMessage("Weekday overtime multiplier must be between 1.0 and 9.99.");

        RuleFor(x => x.WeekendOvertimeMultiplier)
            .InclusiveBetween(1.0m, 9.99m).WithMessage("Weekend overtime multiplier must be between 1.0 and 9.99.");

        RuleFor(x => x.HolidayOvertimeMultiplier)
            .InclusiveBetween(1.0m, 9.99m).WithMessage("Holiday overtime multiplier must be between 1.0 and 9.99.");

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

        // ── DF-23 / ISSUE-068: multi-location geofence — validate each allowed clock-in location.
        RuleForEach(x => x.GeoFenceLocations).SetValidator(new GeofenceLocationValidator());
    }
}

/// <summary>
/// DF-23 / ISSUE-068: shape validation for one ALLOWED clock-in location in the multi-location geofence.
/// </summary>
public sealed class GeofenceLocationValidator : AbstractValidator<GeofenceLocationDto>
{
    public GeofenceLocationValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Allowed-location name is required.")
            .MaximumLength(100).WithMessage("Allowed-location name cannot exceed 100 characters.");

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90m, 90m).WithMessage("Allowed-location latitude must be between -90 and 90.");

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180m, 180m).WithMessage("Allowed-location longitude must be between -180 and 180.");

        RuleFor(x => x.RadiusMeters)
            .GreaterThan(0).WithMessage("Allowed-location radius must be greater than 0 metres.");
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
