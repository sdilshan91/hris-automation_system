using FluentValidation;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Domain.Enums;

namespace HRM.Application.Features.Recruitment.Validators;

/// <summary>
/// Validates scheduling an interview (US-REC-005 FR-1/BR-3/NFR-6). Structural + stateless rules:
/// <list type="bullet">
/// <item>≥1 interviewer (FR-1).</item>
/// <item>Future date — date/time strictly after now (BR-3/NFR-6).</item>
/// <item>Location required for in-person, video link required for video (FR-1).</item>
/// <item>Duration positive and bounded; field length caps.</item>
/// </list>
/// The stateful rules (interviewers must be active employees in the tenant — BR-2; the applicant exists)
/// are enforced in <c>InterviewService</c>, not here, because they require a DB lookup.
/// </summary>
public sealed class ScheduleInterviewValidator : AbstractValidator<ScheduleInterviewCommand>
{
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 8 * 60;   // a full working day
    public const int MaxLocationLength = 300;
    public const int MaxVideoLinkLength = 500;
    public const int MaxNotesLength = 4000;
    public const int MaxInterviewers = 20;

    public ScheduleInterviewValidator()
    {
        RuleFor(x => x.ApplicantId)
            .NotEmpty().WithMessage("Applicant is required.");

        RuleFor(x => x.InterviewType)
            .IsInEnum().WithMessage("A valid interview type is required.");

        // FR-1: at least one interviewer.
        RuleFor(x => x.InterviewerEmployeeIds)
            .NotEmpty().WithMessage("At least one interviewer is required.")
            .Must(ids => ids is null || ids.Count <= MaxInterviewers)
            .WithMessage($"An interview can have at most {MaxInterviewers} interviewers.");

        RuleForEach(x => x.InterviewerEmployeeIds)
            .NotEmpty().WithMessage("Interviewer ids must be non-empty.");

        RuleFor(x => x.InterviewerEmployeeIds)
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("The same interviewer cannot be selected more than once.");

        // BR-3 / NFR-6: the interview date/time must be in the future.
        RuleFor(x => x)
            .Must(BeInTheFuture)
            .WithMessage("The interview must be scheduled for a future date and time.")
            .WithName("ScheduledDate");

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(MinDurationMinutes, MaxDurationMinutes)
            .WithMessage($"Duration must be between {MinDurationMinutes} and {MaxDurationMinutes} minutes.");

        // FR-1: in-person requires a location.
        RuleFor(x => x.Location)
            .NotEmpty().WithMessage("A location is required for an in-person interview.")
            .When(x => x.InterviewType == InterviewType.InPerson);

        // FR-1: video requires a meeting link.
        RuleFor(x => x.VideoLink)
            .NotEmpty().WithMessage("A video meeting link is required for a video interview.")
            .When(x => x.InterviewType == InterviewType.Video);

        RuleFor(x => x.Location)
            .MaximumLength(MaxLocationLength).When(x => !string.IsNullOrWhiteSpace(x.Location));
        RuleFor(x => x.VideoLink)
            .MaximumLength(MaxVideoLinkLength).When(x => !string.IsNullOrWhiteSpace(x.VideoLink));
        RuleFor(x => x.Notes)
            .MaximumLength(MaxNotesLength).When(x => !string.IsNullOrWhiteSpace(x.Notes));
    }

    /// <summary>
    /// BR-3/NFR-6: the date+time, interpreted as UTC (no tenant-timezone infra yet), must be strictly
    /// after now. Shared by the schedule + update validators.
    /// </summary>
    internal static bool BeInTheFuture(DateOnly scheduledDate, TimeOnly startTime)
    {
        var startsAtUtc = DateTime.SpecifyKind(scheduledDate.ToDateTime(startTime), DateTimeKind.Utc);
        return startsAtUtc > DateTime.UtcNow;
    }

    private static bool BeInTheFuture(ScheduleInterviewCommand c) => BeInTheFuture(c.ScheduledDate, c.StartTime);
}
