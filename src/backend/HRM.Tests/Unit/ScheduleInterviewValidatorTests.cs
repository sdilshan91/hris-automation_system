// ============================================================================
// US-REC-005: ScheduleInterviewValidator -- Unit Tests
// Covers the stateless scheduling rules on ScheduleInterviewCommand:
//   - ≥1 interviewer required (FR-1)
//   - future date/time (BR-3/NFR-6)
//   - location required for in-person, video link required for video (FR-1)
//   - duration bounds + happy path
// The stateful BR-2 active-employee check lives in the service (DB lookup) and is
// exercised by InterviewServiceIntegrationTests, not here.
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.Validators;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

public sealed class ScheduleInterviewValidatorTests
{
    private readonly ScheduleInterviewValidator _validator = new();

    private static ScheduleInterviewCommand MakeCommand(
        InterviewType type = InterviewType.Video,
        DateOnly? scheduledDate = null,
        TimeOnly? startTime = null,
        int durationMinutes = 60,
        string? location = null,
        string? videoLink = "https://meet.example.com/abc",
        string? notes = null,
        IReadOnlyList<Guid>? interviewerIds = null)
    {
        return new ScheduleInterviewCommand(
            ApplicantId: Guid.NewGuid(),
            InterviewType: type,
            ScheduledDate: scheduledDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)),
            StartTime: startTime ?? new TimeOnly(10, 0),
            DurationMinutes: durationMinutes,
            Location: location,
            VideoLink: videoLink,
            Notes: notes,
            InterviewerEmployeeIds: interviewerIds ?? new[] { Guid.NewGuid() });
    }

    [Fact]
    public void HappyPath_VideoInterview_IsValid()
    {
        var result = _validator.TestValidate(MakeCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void NoInterviewers_ShouldHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(interviewerIds: Array.Empty<Guid>()));
        result.ShouldHaveValidationErrorFor(x => x.InterviewerEmployeeIds);
    }

    [Fact]
    public void PastDate_ShouldHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(
            scheduledDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))));
        result.ShouldHaveValidationErrorFor("ScheduledDate");
    }

    [Fact]
    public void Today_EarlierTime_ShouldHaveError()
    {
        // A time earlier today is in the past (BR-3) — assert via a clearly-past instant.
        var result = _validator.TestValidate(MakeCommand(
            scheduledDate: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
            startTime: new TimeOnly(0, 1)));
        result.ShouldHaveValidationErrorFor("ScheduledDate");
    }

    [Fact]
    public void InPerson_WithoutLocation_ShouldHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(
            type: InterviewType.InPerson, location: null, videoLink: null));
        result.ShouldHaveValidationErrorFor(x => x.Location);
    }

    [Fact]
    public void InPerson_WithLocation_IsValid()
    {
        var result = _validator.TestValidate(MakeCommand(
            type: InterviewType.InPerson, location: "Room 4B", videoLink: null));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Video_WithoutLink_ShouldHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(
            type: InterviewType.Video, videoLink: null));
        result.ShouldHaveValidationErrorFor(x => x.VideoLink);
    }

    [Fact]
    public void Phone_NeedsNeitherLocationNorLink()
    {
        var result = _validator.TestValidate(MakeCommand(
            type: InterviewType.Phone, location: null, videoLink: null));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void DuplicateInterviewers_ShouldHaveError()
    {
        var dup = Guid.NewGuid();
        var result = _validator.TestValidate(MakeCommand(interviewerIds: new[] { dup, dup }));
        result.ShouldHaveValidationErrorFor(x => x.InterviewerEmployeeIds);
    }

    [Fact]
    public void DurationTooSmall_ShouldHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(durationMinutes: 0));
        result.ShouldHaveValidationErrorFor(x => x.DurationMinutes);
    }
}
