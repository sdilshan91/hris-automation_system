// ============================================================================
// US-ATT-005: ShiftRequestValidator unit tests.
// Covers BR-7 (start == end rejected, night shift allowed), BR-8 (FLEXIBLE requires
// minimum_hours; start/end optional), working-days-required for non-flexible, type
// validity, HH:mm format, and rotation structural rules.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Application.Features.Attendance.Validators;
using HRM.Domain.Entities;

namespace HRM.Tests.Unit;

public sealed class ShiftRequestValidatorTests
{
    private readonly ShiftRequestValidator _validator = new();

    private static ShiftRequest Single(string start = "09:00", string end = "17:00") => new()
    {
        Name = "Day", Type = ShiftType.Single, StartTime = start, EndTime = end,
        BreakDurationMinutes = 60, GracePeriodMinutes = 10, WorkingDays = new[] { 1, 2, 3 },
    };

    [Fact]
    public void Br7_zero_duration_shift_is_rejected()
    {
        var result = _validator.TestValidate(Single("09:00", "09:00"));
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Br7_night_shift_end_before_start_is_allowed()
    {
        var result = _validator.TestValidate(Single("22:00", "06:00"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Br8_flexible_requires_minimum_hours()
    {
        var flexible = new ShiftRequest
        {
            Name = "Flex", Type = ShiftType.Flexible, MinimumHours = null,
        };
        var result = _validator.TestValidate(flexible);
        result.ShouldHaveValidationErrorFor(x => x.MinimumHours);
    }

    [Fact]
    public void Br8_flexible_does_not_require_start_end_or_working_days()
    {
        var flexible = new ShiftRequest
        {
            Name = "Flex", Type = ShiftType.Flexible, MinimumHours = 6m,
        };
        var result = _validator.TestValidate(flexible);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Non_flexible_requires_working_days()
    {
        var s = Single();
        var noDays = s with { WorkingDays = Array.Empty<int>() };
        var result = _validator.TestValidate(noDays);
        result.ShouldHaveValidationErrorFor(x => x.WorkingDays);
    }

    [Fact]
    public void Invalid_type_is_rejected()
    {
        var s = Single() with { Type = "WEEKEND" };
        var result = _validator.TestValidate(s);
        result.ShouldHaveValidationErrorFor(x => x.Type);
    }

    [Fact]
    public void Invalid_time_format_is_rejected()
    {
        var s = Single() with { StartTime = "9am" };
        var result = _validator.TestValidate(s);
        result.ShouldHaveValidationErrorFor(x => x.StartTime);
    }

    [Fact]
    public void Rotating_requires_rotation_with_steps_summing_to_cycle()
    {
        var bad = new ShiftRequest
        {
            Name = "Rot", Type = ShiftType.Rotating, StartTime = "09:00", EndTime = "17:00",
            WorkingDays = new[] { 1, 2, 3, 4, 5 },
            Rotation = new RotationRequest
            {
                CycleLengthDays = 14,
                ReferenceStartDate = new DateOnly(2026, 1, 5),
                Steps = new[]
                {
                    new RotationStepRequest { Order = 0, ShiftId = Guid.NewGuid(), DurationDays = 7 },
                    // missing the second 7-day step → sum (7) != cycle (14)
                },
            },
        };
        var result = _validator.TestValidate(bad);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rotating_with_matching_step_sum_is_valid()
    {
        var ok = new ShiftRequest
        {
            Name = "Rot", Type = ShiftType.Rotating, StartTime = "09:00", EndTime = "17:00",
            WorkingDays = new[] { 1, 2, 3, 4, 5 },
            Rotation = new RotationRequest
            {
                CycleLengthDays = 14,
                ReferenceStartDate = new DateOnly(2026, 1, 5),
                Steps = new[]
                {
                    new RotationStepRequest { Order = 0, ShiftId = Guid.NewGuid(), DurationDays = 7 },
                    new RotationStepRequest { Order = 1, ShiftId = Guid.NewGuid(), DurationDays = 7 },
                },
            },
        };
        var result = _validator.TestValidate(ok);
        result.IsValid.Should().BeTrue();
    }
}
