using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Application.Features.Attendance.Validators;
using Xunit;

namespace HRM.Tests.Unit;

/// <summary>
/// BUG-522. The three overtime multipliers are <c>numeric(3,2)</c> columns
/// (<c>AttendanceSettingsConfiguration.cs:91,96,101</c>), so 9.99 is the largest storable value — but the
/// validator's upper bound was <c>10m</c>. Exactly <c>10.00</c> therefore passed validation and failed on
/// INSERT with a Postgres 22003 numeric overflow, surfacing as a **500 rather than a 400**.
///
/// <para>The gap was invisible because <b>this validator had no tests at all</b>. A bound and the column it
/// is supposed to mirror can drift indefinitely when nothing asserts they agree, which is exactly what
/// happened here — and the attendance-settings screen (ISSUE-438) is what finally made the value reachable
/// by a user rather than only by a hand-written API call.</para>
///
/// <para>These arms pin the boundary from BOTH sides. An upper-bound test that only checks the rejected
/// value would keep passing if someone lowered the bound to 2.0 and broke every legitimate 2.5x weekend
/// premium, so the accepted-value arms are load-bearing, not padding.</para>
/// </summary>
public sealed class AttendanceSettingsMultiplierBoundsTests
{
    private static readonly AttendanceSettingsPolicyValidator Validator = new();

    /// <summary>A DTO that is valid apart from whatever the test under way overrides.</summary>
    private static AttendanceSettingsDto Valid(
        decimal weekday = 1.5m, decimal weekend = 2.0m, decimal holiday = 2.5m) => new()
    {
        WeekdayOvertimeMultiplier = weekday,
        WeekendOvertimeMultiplier = weekend,
        HolidayOvertimeMultiplier = holiday,
    };

    [Theory]
    [InlineData(10.00)]   // the exact value that used to pass validation and then 22003 on insert
    [InlineData(10.01)]
    [InlineData(99.99)]
    public void A_weekday_multiplier_above_the_column_capacity_is_rejected(decimal value)
    {
        Validator.TestValidate(Valid(weekday: value))
            .ShouldHaveValidationErrorFor(x => x.WeekdayOvertimeMultiplier)
            .WithErrorMessage("Weekday overtime multiplier must be between 1.0 and 9.99.");
    }

    [Theory]
    [InlineData(10.00)]
    [InlineData(10.00)]
    public void The_weekend_and_holiday_multipliers_share_the_same_ceiling(decimal value)
    {
        Validator.TestValidate(Valid(weekend: value))
            .ShouldHaveValidationErrorFor(x => x.WeekendOvertimeMultiplier);

        Validator.TestValidate(Valid(holiday: value))
            .ShouldHaveValidationErrorFor(x => x.HolidayOvertimeMultiplier);
    }

    [Theory]
    [InlineData(9.99)]    // the largest value numeric(3,2) can hold — must still be accepted
    [InlineData(2.50)]
    [InlineData(1.50)]
    [InlineData(1.00)]
    public void A_multiplier_the_column_can_store_is_accepted(decimal value)
    {
        Validator.TestValidate(Valid(weekday: value))
            .ShouldNotHaveValidationErrorFor(x => x.WeekdayOvertimeMultiplier);
    }

    [Fact]
    public void A_multiplier_below_one_is_still_rejected()
    {
        // Guards the LOWER bound, which this change did not touch: paying overtime at less than
        // regular time is never a legitimate configuration.
        Validator.TestValidate(Valid(weekday: 0.9m))
            .ShouldHaveValidationErrorFor(x => x.WeekdayOvertimeMultiplier);
    }

    [Fact]
    public void The_validator_bound_matches_what_the_column_can_actually_store()
    {
        // The assertion that would have caught BUG-522 on the day the bound was written. 9.99 is
        // numeric(3,2)'s maximum; if someone widens the column they must widen this together with it.
        const decimal columnMax = 9.99m;

        Validator.TestValidate(Valid(weekday: columnMax))
            .ShouldNotHaveValidationErrorFor(x => x.WeekdayOvertimeMultiplier);

        Validator.TestValidate(Valid(weekday: columnMax + 0.01m))
            .ShouldHaveValidationErrorFor(x => x.WeekdayOvertimeMultiplier);
    }
}
