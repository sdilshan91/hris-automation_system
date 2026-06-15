using HRM.Domain.Enums;

namespace HRM.Application.Features.Recruitment.DTOs;

/// <summary>
/// Service-layer input for scheduling an interview (US-REC-005 FR-1). Carried by
/// <c>ScheduleInterviewCommand</c>. The round number is auto-assigned by the service (FR-2), so it is not
/// part of the input.
/// </summary>
public sealed record ScheduleInterviewInput
{
    public Guid ApplicantId { get; init; }
    public InterviewType InterviewType { get; init; }
    public DateOnly ScheduledDate { get; init; }
    public TimeOnly StartTime { get; init; }
    public int DurationMinutes { get; init; } = 60;
    public string? Location { get; init; }
    public string? VideoLink { get; init; }
    public string? Notes { get; init; }

    /// <summary>Interviewer employee ids (FR-1, ≥1 required, BR-2 must be active employees in the tenant).</summary>
    public IReadOnlyList<Guid> InterviewerEmployeeIds { get; init; } = [];
}

/// <summary>
/// Service-layer input for rescheduling/updating an interview (US-REC-005 AC-3/BR-6). All schedulable
/// fields can change, including the interviewer set. The reminder job is swapped (delete + recreate) when
/// the timing changes (BR-6).
/// </summary>
public sealed record UpdateInterviewInput
{
    public InterviewType InterviewType { get; init; }
    public DateOnly ScheduledDate { get; init; }
    public TimeOnly StartTime { get; init; }
    public int DurationMinutes { get; init; } = 60;
    public string? Location { get; init; }
    public string? VideoLink { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<Guid> InterviewerEmployeeIds { get; init; } = [];
}

/// <summary>A single interviewer on an interview (employee id + display name + work email), FR-1/BR-7.</summary>
public sealed record InterviewerDto
{
    public Guid EmployeeId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}

/// <summary>
/// Full interview shape returned to the FE (US-REC-005 FR-1/FR-2/FR-6). camelCase on the wire. Carries
/// the schedule, type/status (string companions for display), the interviewer list, and the optional
/// reminder job id. <see cref="Warnings"/> surfaces soft scheduling-conflict warnings (FR-7) on the
/// create/update responses (never on plain reads).
/// </summary>
public sealed record InterviewDto
{
    public Guid Id { get; init; }
    public Guid ApplicantId { get; init; }
    public Guid VacancyId { get; init; }
    public string? VacancyTitle { get; init; }
    public string? ApplicantName { get; init; }
    public int RoundNumber { get; init; }

    public InterviewType InterviewType { get; init; }
    public string InterviewTypeName { get; init; } = string.Empty;

    public DateOnly ScheduledDate { get; init; }
    public TimeOnly StartTime { get; init; }
    public int DurationMinutes { get; init; }

    public string? Location { get; init; }
    public string? VideoLink { get; init; }
    public string? Notes { get; init; }

    public InterviewStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;

    public string? ReminderJobId { get; init; }

    public IReadOnlyList<InterviewerDto> Interviewers { get; init; } = [];

    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Soft, overridable scheduling-conflict warnings (US-REC-005 FR-7) — populated on create/update
    /// responses when a selected interviewer already has an overlapping Scheduled interview. The
    /// interview is STILL saved (override allowed); these are advisory only. Empty on plain reads.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Filter parameters for the interview calendar query (US-REC-005 FR-5): optional interviewer, vacancy,
/// date range, and status. All optional; an empty filter returns every scheduled interview in the tenant.
/// </summary>
public sealed record InterviewCalendarFilter
{
    public Guid? InterviewerEmployeeId { get; init; }
    public Guid? VacancyId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public InterviewStatus? Status { get; init; }
}
