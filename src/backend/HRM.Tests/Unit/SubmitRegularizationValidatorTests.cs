// ============================================================================
// US-ATT-003: SubmitRegularization validator unit tests (FR-5, BR-7, §7 conditional times).
// These shape-level rules are enforced by the FluentValidation validator in the MediatR
// pipeline (ValidationBehavior throws ValidationException -> 400). DB/relative-time rules
// (lookback, duplicate, payroll lock, future date) are tested in the service tests.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Attendance.Commands;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Application.Features.Attendance.Validators;
using HRM.Domain.Entities;

namespace HRM.Tests.Unit;

public sealed class SubmitRegularizationValidatorTests
{
    private readonly SubmitRegularizationValidator _validator = new();

    // Corrected times are now wall-clock "HH:mm" strings paired with the date; the validator combines
    // date + HH:mm (as UTC) before applying FR-5/future checks. A date safely in the past keeps the
    // not-in-future rule from interfering with the consistency-focused tests below.
    private static SubmitRegularizationCommand Command(
        string type = RegularizationType.MissedBoth,
        string? clockIn = null,
        string? clockOut = null,
        string reason = "Forgot to clock in and out")
    {
        var date = new DateOnly(2026, 6, 10);
        return new SubmitRegularizationCommand(new SubmitRegularizationRequest
        {
            Date = date,
            RegularizationType = type,
            RequestedClockIn = clockIn ?? "09:00",
            RequestedClockOut = clockOut ?? "17:00",
            Reason = reason,
        });
    }

    [Fact]
    public void Valid_MissedBoth_Passes()
    {
        _validator.TestValidate(Command()).ShouldNotHaveAnyValidationErrors();
    }

    // ── BR-7: reason minimum length ──────────────────────────────────────────

    [Fact]
    public void Reason_ShorterThanTen_Fails()
    {
        _validator.TestValidate(Command(reason: "too short"))   // 9 chars
            .ShouldHaveValidationErrorFor(x => x.Request.Reason);
    }

    [Fact]
    public void Reason_Empty_Fails()
    {
        _validator.TestValidate(Command(reason: "   "))
            .ShouldHaveValidationErrorFor(x => x.Request.Reason);
    }

    // ── FR-5: clock-in must be before clock-out ──────────────────────────────

    [Fact]
    public void ClockIn_AfterClockOut_Fails()
    {
        var result = _validator.TestValidate(Command(
            clockIn: "18:00",
            clockOut: "09:00"));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ClockIn_EqualToClockOut_Fails()
    {
        var result = _validator.TestValidate(Command(clockIn: "12:00", clockOut: "12:00"));

        result.IsValid.Should().BeFalse();
    }

    // ── §7: corrected times must be valid 24-hour HH:mm ──────────────────────

    [Fact]
    public void ClockIn_NotHhMmFormat_Fails()
    {
        // An ISO timestamp (the OLD contract) is no longer a valid value — must be "HH:mm".
        var result = _validator.TestValidate(Command(clockIn: "2026-06-10T09:00:00Z"));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ClockOut_OutOfRangeTime_Fails()
    {
        var result = _validator.TestValidate(Command(clockOut: "25:61"));

        result.IsValid.Should().BeFalse();
    }

    // ── §7: conditionally-required corrected times ───────────────────────────

    [Fact]
    public void MissedClockIn_WithoutClockIn_Fails()
    {
        var cmd = new SubmitRegularizationCommand(new SubmitRegularizationRequest
        {
            Date = new DateOnly(2026, 6, 10),
            RegularizationType = RegularizationType.MissedClockIn,
            RequestedClockIn = null,
            RequestedClockOut = null,
            Reason = "Forgot to clock in this morning",
        });

        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.RequestedClockIn);
    }

    [Fact]
    public void MissedClockOut_WithoutClockOut_Fails()
    {
        var cmd = new SubmitRegularizationCommand(new SubmitRegularizationRequest
        {
            Date = new DateOnly(2026, 6, 10),
            RegularizationType = RegularizationType.MissedClockOut,
            RequestedClockIn = null,
            RequestedClockOut = null,
            Reason = "Forgot to clock out this evening",
        });

        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.RequestedClockOut);
    }

    [Fact]
    public void InvalidType_Fails()
    {
        _validator.TestValidate(Command(type: "BOGUS"))
            .ShouldHaveValidationErrorFor(x => x.Request.RegularizationType);
    }
}
