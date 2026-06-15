using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A scheduled interview for an <see cref="Applicant"/> against a <see cref="Vacancy"/> (US-REC-005).
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> and the EF global query filter +
/// <c>TenantInterceptor</c> (this codebase enforces tenant isolation with EF query filters, not Postgres
/// RLS — RLS/NFR-2 is a separate deferred story; see the module note). Maps to the "interview" table.
///
/// Multiple rounds per applicant are supported (FR-2): each round is its own Interview row with an
/// auto-incremented <see cref="RoundNumber"/> and its own interviewer set (<see cref="Interviewers"/>).
/// </summary>
public sealed class Interview : BaseEntity
{
    /// <summary>FK to the <see cref="Applicant"/> being interviewed (FR-1, required). Plain UUID column.</summary>
    public Guid ApplicantId { get; set; }

    /// <summary>FK to the owning <see cref="Vacancy"/> (FR-1, required). Plain UUID column.</summary>
    public Guid VacancyId { get; set; }

    /// <summary>
    /// 1-based interview round for this applicant on this vacancy (FR-2). Auto-incremented when a new
    /// interview is scheduled (max existing round + 1).
    /// </summary>
    public int RoundNumber { get; set; } = 1;

    /// <summary>Interview medium (FR-1). Drives the location/video-link requirement.</summary>
    public InterviewType InterviewType { get; set; } = InterviewType.Video;

    /// <summary>
    /// Calendar date of the interview (FR-1, required). The full start instant is
    /// <see cref="ScheduledDate"/> at <see cref="StartTime"/>; the reminder fire-time is computed from it.
    /// </summary>
    public DateOnly ScheduledDate { get; set; }

    /// <summary>Start time of the interview on <see cref="ScheduledDate"/> (FR-1, required).</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Planned duration in minutes (FR-1, required, default 60). Used for conflict overlap (FR-7).</summary>
    public int DurationMinutes { get; set; } = 60;

    /// <summary>Physical location (FR-1). Required for <see cref="InterviewType.InPerson"/>, else optional.</summary>
    public string? Location { get; set; }

    /// <summary>Video meeting link (FR-1). Required for <see cref="InterviewType.Video"/>, else optional.</summary>
    public string? VideoLink { get; set; }

    /// <summary>Optional notes for interviewers (FR-1). Stored as text.</summary>
    public string? Notes { get; set; }

    /// <summary>Interview lifecycle status (FR-6). Always <see cref="InterviewStatus.Scheduled"/> on create.</summary>
    public InterviewStatus Status { get; set; } = InterviewStatus.Scheduled;

    /// <summary>
    /// Id of the scheduled Hangfire reminder job (FR-4/BR-6). Set when a reminder is scheduled, swapped on
    /// reschedule (delete + recreate), and cleared on cancel. Null when no reminder is active (e.g. the
    /// fire-time would already be in the past, or the job client is unavailable — tests/dev).
    /// </summary>
    public string? ReminderJobId { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    /// <summary>The interviewers assigned to this interview (FR-1, ≥1 required). Junction rows.</summary>
    public ICollection<InterviewInterviewer> Interviewers { get; set; } = new List<InterviewInterviewer>();

    /// <summary>
    /// The UTC start instant of the interview, treating the stored date/time as UTC (the codebase has no
    /// tenant-timezone infra yet — same deferral as the attendance jobs). Used for the reminder fire-time
    /// and conflict-overlap math (FR-4/FR-7).
    /// </summary>
    public DateTime StartsAtUtc =>
        DateTime.SpecifyKind(ScheduledDate.ToDateTime(StartTime), DateTimeKind.Utc);

    /// <summary>The UTC end instant (start + duration). Used for conflict-overlap math (FR-7).</summary>
    public DateTime EndsAtUtc => StartsAtUtc.AddMinutes(DurationMinutes);
}
