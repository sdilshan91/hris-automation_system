// ============================================================================
// US-ONB-006: RecordExitInterviewCommand validator tests (structural rules, §7).
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Onboarding.Commands;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Application.Features.Onboarding.Validators;

namespace HRM.Tests.Unit;

public sealed class RecordExitInterviewValidatorTests
{
    private readonly RecordExitInterviewValidator _validator = new();

    private static RecordExitInterviewCommand Cmd(
        Guid? offboardingId = null,
        string mode = "hr_conducted",
        DateOnly? date = null,
        int? overall = 4,
        string? comments = null,
        IReadOnlyList<ExitInterviewResponseInput>? responses = null)
    {
        var input = new RecordExitInterviewInput(
            offboardingId ?? Guid.NewGuid(),
            mode,
            date ?? DateOnly.FromDateTime(DateTime.UtcNow),
            responses ?? new[] { new ExitInterviewResponseInput(Guid.NewGuid(), 5, null, null) },
            overall, true, comments);
        return new RecordExitInterviewCommand(input, IsSelfService: false, AllowEdit: false);
    }

    [Fact]
    public void Valid_command_passes()
    {
        var result = _validator.TestValidate(Cmd());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_offboarding_id_fails()
    {
        var result = _validator.TestValidate(Cmd(offboardingId: Guid.Empty));
        result.ShouldHaveValidationErrorFor("Input.OffboardingId");
    }

    [Fact]
    public void Invalid_interview_mode_fails()
    {
        var result = _validator.TestValidate(Cmd(mode: "robot_conducted"));
        result.ShouldHaveValidationErrorFor("Input.InterviewMode");
    }

    [Theory]
    [InlineData("hr_conducted")]
    [InlineData("self_service")]
    public void Valid_interview_modes_pass(string mode)
    {
        var result = _validator.TestValidate(Cmd(mode: mode));
        result.ShouldNotHaveValidationErrorFor("Input.InterviewMode");
    }

    [Fact]
    public void Interview_date_in_the_future_fails()
    {
        var result = _validator.TestValidate(Cmd(date: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))));
        result.ShouldHaveValidationErrorFor("Input.InterviewDate");
    }

    [Fact]
    public void Interview_date_today_passes()
    {
        var result = _validator.TestValidate(Cmd(date: DateOnly.FromDateTime(DateTime.UtcNow)));
        result.ShouldNotHaveValidationErrorFor("Input.InterviewDate");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Overall_rating_out_of_range_fails(int rating)
    {
        var result = _validator.TestValidate(Cmd(overall: rating));
        result.ShouldHaveValidationErrorFor("Input.OverallExperienceRating");
    }

    [Fact]
    public void Null_overall_rating_passes()
    {
        var result = _validator.TestValidate(Cmd(overall: null));
        result.ShouldNotHaveValidationErrorFor("Input.OverallExperienceRating");
    }

    [Fact]
    public void Additional_comments_over_5000_chars_fails()
    {
        var result = _validator.TestValidate(Cmd(comments: new string('x', 5001)));
        result.ShouldHaveValidationErrorFor("Input.AdditionalComments");
    }

    [Fact]
    public void Response_rating_out_of_range_fails()
    {
        var responses = new[] { new ExitInterviewResponseInput(Guid.NewGuid(), 9, null, null) };
        var result = _validator.TestValidate(Cmd(responses: responses));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Response_free_text_over_2000_chars_fails()
    {
        var responses = new[] { new ExitInterviewResponseInput(Guid.NewGuid(), null, null, new string('y', 2001)) };
        var result = _validator.TestValidate(Cmd(responses: responses));
        result.IsValid.Should().BeFalse();
    }
}
