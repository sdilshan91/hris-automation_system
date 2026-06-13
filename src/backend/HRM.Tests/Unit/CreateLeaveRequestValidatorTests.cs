// ============================================================================
// US-LV-003: CreateLeaveRequestValidator shape-level tests (FR-1, FR-5, §10).
// Required fields, date ordering, half-day single-day + session, attachment
// count/type limits.
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.LeaveRequests.Commands;
using HRM.Application.Features.LeaveRequests.Validators;

namespace HRM.Tests.Unit;

public sealed class CreateLeaveRequestValidatorTests
{
    private readonly CreateLeaveRequestValidator _validator = new();

    private static CreateLeaveRequestCommand Make(
        Guid? leaveTypeId = null,
        DateOnly? start = null,
        DateOnly? end = null,
        bool isHalfDay = false,
        string? session = null,
        string? reason = "Vacation",
        IReadOnlyList<string>? attachments = null) => new(
        LeaveTypeId: leaveTypeId ?? Guid.NewGuid(),
        StartDate: start ?? new DateOnly(2026, 6, 15),
        EndDate: end ?? new DateOnly(2026, 6, 17),
        IsHalfDay: isHalfDay,
        HalfDaySession: session,
        Reason: reason,
        Attachments: attachments);

    [Fact]
    public void ValidFullDayRequest_Passes()
    {
        _validator.TestValidate(Make()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyLeaveTypeId_Fails()
    {
        _validator.TestValidate(Make(leaveTypeId: Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.LeaveTypeId);
    }

    [Fact]
    public void EndBeforeStart_Fails()
    {
        var cmd = Make(start: new DateOnly(2026, 6, 17), end: new DateOnly(2026, 6, 15));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.EndDate);
    }

    [Fact]
    public void HalfDay_MultiDay_Fails()
    {
        var cmd = Make(start: new DateOnly(2026, 6, 15), end: new DateOnly(2026, 6, 16),
            isHalfDay: true, session: "AM");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.EndDate);
    }

    [Fact]
    public void HalfDay_MissingSession_Fails()
    {
        var cmd = Make(start: new DateOnly(2026, 6, 15), end: new DateOnly(2026, 6, 15),
            isHalfDay: true, session: null);
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.HalfDaySession);
    }

    [Fact]
    public void HalfDay_InvalidSession_Fails()
    {
        var cmd = Make(start: new DateOnly(2026, 6, 15), end: new DateOnly(2026, 6, 15),
            isHalfDay: true, session: "EVENING");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.HalfDaySession);
    }

    [Fact]
    public void HalfDay_ValidSession_Passes()
    {
        var cmd = Make(start: new DateOnly(2026, 6, 15), end: new DateOnly(2026, 6, 15),
            isHalfDay: true, session: "pm");
        _validator.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void FullDay_WithSession_Fails()
    {
        var cmd = Make(isHalfDay: false, session: "AM");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.HalfDaySession);
    }

    [Fact]
    public void TooManyAttachments_Fails()
    {
        var cmd = Make(attachments: ["a.pdf", "b.pdf", "c.pdf", "d.pdf"]);
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Attachments);
    }

    [Fact]
    public void DisallowedAttachmentType_Fails()
    {
        var cmd = Make(attachments: ["report.exe"]);
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor("Attachments[0]");
    }

    [Fact]
    public void AllowedAttachmentTypes_WithQueryString_Pass()
    {
        var cmd = Make(attachments: ["https://x/cert.PDF?sig=abc", "img.jpg", "scan.png"]);
        _validator.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }
}
