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
        IReadOnlyList<Guid>? attachmentIds = null) => new(
        LeaveTypeId: leaveTypeId ?? Guid.NewGuid(),
        StartDate: start ?? new DateOnly(2026, 6, 15),
        EndDate: end ?? new DateOnly(2026, 6, 17),
        IsHalfDay: isHalfDay,
        HalfDaySession: session,
        Reason: reason,
        AttachmentIds: attachmentIds);

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
        var cmd = Make(attachmentIds: [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]);
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.AttachmentIds);
    }

    // ISSUE-036: this arm used to assert "report.exe" was rejected by an extension check on a client string.
    // File TYPE is now validated at UPLOAD time against the real bytes (declared-MIME allow-list + extension
    // allow-list + FileSignatureValidator magic-byte sniff) — see
    // LeaveAttachmentIntegrationTests.Upload_DisallowedMimeType_Rejected_ISSUE036, which is a strictly
    // stronger check than a string suffix. What the validator can still catch on the shape is a malformed
    // (empty) attachment id.
    [Fact]
    public void EmptyAttachmentId_Fails()
    {
        var cmd = Make(attachmentIds: [Guid.Empty]);
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor("AttachmentIds[0]");
    }

    [Fact]
    public void ValidAttachmentIds_Pass()
    {
        var cmd = Make(attachmentIds: [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]);
        _validator.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }
}
