// ============================================================================
// US-LV-005: Approve/Reject validator shape-level tests (FR-1, FR-2, BR-2).
//
// ApproveLeaveRequestValidator: id required, comment optional with a length cap.
// RejectLeaveRequestValidator: id required, reason mandatory (BR-2, AC-2) with a
// length cap. Mirrors CreateLeaveRequestValidatorTests / LeaveTypeValidatorTests.
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.LeaveRequests.Commands;
using HRM.Application.Features.LeaveRequests.Validators;

namespace HRM.Tests.Unit;

public sealed class ApproveLeaveRequestValidatorTests
{
    private readonly ApproveLeaveRequestValidator _validator = new();

    [Fact]
    public void ValidApproval_NoComment_Passes()
    {
        var cmd = new ApproveLeaveRequestCommand(Guid.NewGuid(), Comment: null);
        _validator.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ValidApproval_WithComment_Passes()
    {
        var cmd = new ApproveLeaveRequestCommand(Guid.NewGuid(), Comment: "Approved, enjoy your break.");
        _validator.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyLeaveRequestId_Fails()
    {
        var cmd = new ApproveLeaveRequestCommand(Guid.Empty, Comment: null);
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.LeaveRequestId);
    }

    [Fact]
    public void CommentExceedingMaxLength_Fails()
    {
        var cmd = new ApproveLeaveRequestCommand(Guid.NewGuid(), Comment: new string('a', 2001));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Comment);
    }
}

public sealed class RejectLeaveRequestValidatorTests
{
    private readonly RejectLeaveRequestValidator _validator = new();

    [Fact]
    public void ValidRejection_WithReason_Passes()
    {
        var cmd = new RejectLeaveRequestCommand(Guid.NewGuid(), Reason: "Insufficient coverage that week.");
        _validator.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyLeaveRequestId_Fails()
    {
        var cmd = new RejectLeaveRequestCommand(Guid.Empty, Reason: "Some reason");
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.LeaveRequestId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingReason_Fails(string reason)
    {
        var cmd = new RejectLeaveRequestCommand(Guid.NewGuid(), reason);
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void ReasonExceedingMaxLength_Fails()
    {
        var cmd = new RejectLeaveRequestCommand(Guid.NewGuid(), new string('a', 2001));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Reason);
    }
}
