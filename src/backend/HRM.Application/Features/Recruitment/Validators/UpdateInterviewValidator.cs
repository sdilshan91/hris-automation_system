using FluentValidation;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Recruitment.Validators;

/// <summary>
/// Validates rescheduling/updating an interview (US-REC-005 AC-3/FR-1/BR-3/NFR-6). Same field rules as
/// <see cref="ScheduleInterviewValidator"/> (≥1 interviewer, future date, type-conditional location/video
/// link, duration + length caps). The stateful BR-2 active-employee check + interview existence are
/// enforced in <c>InterviewService</c>.
/// </summary>
public sealed class UpdateInterviewValidator : AbstractValidator<UpdateInterviewCommand>
{
    public UpdateInterviewValidator()
    {
        RuleFor(x => x.InterviewId)
            .NotEmpty().WithMessage("Interview is required.");

        RuleFor(x => x.InterviewType)
            .IsInEnum().WithMessage("A valid interview type is required.");

        RuleFor(x => x.InterviewerEmployeeIds)
            .NotEmpty().WithMessage("At least one interviewer is required.")
            .Must(ids => ids is null || ids.Count <= ScheduleInterviewValidator.MaxInterviewers)
            .WithMessage($"An interview can have at most {ScheduleInterviewValidator.MaxInterviewers} interviewers.");

        RuleForEach(x => x.InterviewerEmployeeIds)
            .NotEmpty().WithMessage("Interviewer ids must be non-empty.");

        RuleFor(x => x.InterviewerEmployeeIds)
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("The same interviewer cannot be selected more than once.");

        RuleFor(x => x)
            .Must(c => ScheduleInterviewValidator.BeInTheFuture(c.ScheduledDate, c.StartTime))
            .WithMessage("The interview must be rescheduled to a future date and time.")
            .WithName("ScheduledDate");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(ScheduleInterviewValidator.MinDurationMinutes, ScheduleInterviewValidator.MaxDurationMinutes)
            .WithMessage($"Duration must be between {ScheduleInterviewValidator.MinDurationMinutes} and {ScheduleInterviewValidator.MaxDurationMinutes} minutes.");

        RuleFor(x => x.Location)
            .NotEmpty().WithMessage("A location is required for an in-person interview.")
            .When(x => x.InterviewType == InterviewType.InPerson);

        RuleFor(x => x.VideoLink)
            .NotEmpty().WithMessage("A video meeting link is required for a video interview.")
            .When(x => x.InterviewType == InterviewType.Video);

        // ISSUE-114: a supplied video link must be a well-formed absolute http(s) URL.
        RuleFor(x => x.VideoLink)
            .Must(ScheduleInterviewValidator.BeAValidVideoLink).WithMessage("The video meeting link must be a valid http(s) URL.")
            .When(x => !string.IsNullOrWhiteSpace(x.VideoLink));

        RuleFor(x => x.Location)
            .MaximumLength(ScheduleInterviewValidator.MaxLocationLength).When(x => !string.IsNullOrWhiteSpace(x.Location));
        RuleFor(x => x.VideoLink)
            .MaximumLength(ScheduleInterviewValidator.MaxVideoLinkLength).When(x => !string.IsNullOrWhiteSpace(x.VideoLink));
        RuleFor(x => x.Notes)
            .MaximumLength(ScheduleInterviewValidator.MaxNotesLength).When(x => !string.IsNullOrWhiteSpace(x.Notes));
    }
}
