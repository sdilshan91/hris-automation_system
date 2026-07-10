// ============================================================================
// US-TRN-001 — FluentValidation rules for CreateCourseCommand / UpdateCourseCommand, plus the pure
// course status-transition matrix (TrainingService.IsLegalTransition, AC-2). InMemory-free unit tests.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Training.Commands;
using HRM.Application.Features.Training.DTOs;
using HRM.Application.Features.Training.Validators;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-TRN-001")]
public sealed class TrainingValidatorTests
{
    private readonly CreateCourseValidator _create = new();
    private readonly UpdateCourseValidator _update = new();

    private static CreateCourseCommand Create(
        string title = "Safety 101",
        string mode = "Online",
        int? capacity = null,
        decimal? durationHours = null,
        DateOnly? start = null,
        DateOnly? end = null) => new(new CreateCourseRequest
        {
            Title = title,
            Mode = mode,
            Capacity = capacity,
            DurationHours = durationHours,
            StartDate = start,
            EndDate = end,
        });

    [Fact]
    public void Create_EmptyTitle_Fails()
        => _create.TestValidate(Create(title: "")).ShouldHaveValidationErrorFor(x => x.Request.Title);

    [Fact]
    public void Create_InvalidMode_Fails()
        => _create.TestValidate(Create(mode: "Telepathy")).ShouldHaveValidationErrorFor(x => x.Request.Mode);

    [Theory]
    [InlineData("InPerson")]
    [InlineData("Online")]
    [InlineData("Hybrid")]
    public void Create_ValidMode_Passes(string mode)
        => _create.TestValidate(Create(mode: mode)).ShouldNotHaveValidationErrorFor(x => x.Request.Mode);

    [Fact]
    public void Create_NegativeCapacity_Fails()
        => _create.TestValidate(Create(capacity: -1)).ShouldHaveValidationErrorFor(x => x.Request.Capacity);

    [Fact]
    public void Create_ZeroCapacity_Passes()
        => _create.TestValidate(Create(capacity: 0)).ShouldNotHaveValidationErrorFor(x => x.Request.Capacity);

    [Fact]
    public void Create_NonPositiveDuration_Fails()
        => _create.TestValidate(Create(durationHours: 0)).ShouldHaveValidationErrorFor(x => x.Request.DurationHours);

    [Fact]
    public void Create_EndBeforeStart_Fails()
        => _create.TestValidate(Create(start: new DateOnly(2026, 7, 10), end: new DateOnly(2026, 7, 1)))
            .ShouldHaveValidationErrorFor(x => x.Request);

    [Fact]
    public void Create_EndAfterStart_Passes()
        => _create.TestValidate(Create(start: new DateOnly(2026, 7, 1), end: new DateOnly(2026, 7, 10)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Update_EmptyTitle_Fails()
    {
        var cmd = new UpdateCourseCommand(Guid.NewGuid(), new UpdateCourseRequest { Title = "", Mode = "Online" });
        _update.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.Title);
    }

    // ── Status-transition matrix (AC-2) ─────────────────────────────────────────

    [Theory]
    [InlineData(CourseStatus.Draft, CourseStatus.Open, true)]
    [InlineData(CourseStatus.Draft, CourseStatus.Cancelled, true)]
    [InlineData(CourseStatus.Draft, CourseStatus.Completed, false)]
    [InlineData(CourseStatus.Draft, CourseStatus.Closed, false)]
    [InlineData(CourseStatus.Open, CourseStatus.Closed, true)]
    [InlineData(CourseStatus.Open, CourseStatus.Cancelled, true)]
    [InlineData(CourseStatus.Open, CourseStatus.Completed, true)]
    [InlineData(CourseStatus.Open, CourseStatus.Draft, false)]
    [InlineData(CourseStatus.Closed, CourseStatus.Completed, true)]
    [InlineData(CourseStatus.Closed, CourseStatus.Cancelled, true)]
    [InlineData(CourseStatus.Closed, CourseStatus.Open, false)]
    [InlineData(CourseStatus.Cancelled, CourseStatus.Open, false)]
    [InlineData(CourseStatus.Completed, CourseStatus.Open, false)]
    public void IsLegalTransition_MatchesMatrix(CourseStatus from, CourseStatus to, bool expected)
        => TrainingService.IsLegalTransition(from, to).Should().Be(expected);
}
