using FluentValidation;
using HRM.Application.Features.Attendance.Commands;

namespace HRM.Application.Features.Attendance.Validators;

/// <summary>
/// CAL-4b / US-ATT-011 AC-3: validates the per-Location attendance-policy override upsert payload. Shares
/// the 24-field rule set with the tenant-default upsert (<see cref="AttendanceSettingsPolicyValidator"/>)
/// and adds the route's LocationId shape check.
///
/// <para>Whether that LocationId actually EXISTS, is same-tenant and is active is a DB-dependent check and
/// lives in <c>AttendanceSettingsService</c> (rejected as 400 "invalid_location") — FluentValidation has
/// no DB access.</para>
/// </summary>
public sealed class UpsertLocationAttendanceSettingsValidator
    : AbstractValidator<UpsertLocationAttendanceSettingsCommand>
{
    public UpsertLocationAttendanceSettingsValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty().WithMessage("Location id is required.");

        RuleFor(x => x.Settings).NotNull();
        RuleFor(x => x.Settings).SetValidator(new AttendanceSettingsPolicyValidator())
            .When(x => x.Settings is not null);
    }
}
