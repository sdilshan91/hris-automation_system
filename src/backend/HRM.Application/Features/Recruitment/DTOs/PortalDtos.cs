using HRM.Domain.Enums;

namespace HRM.Application.Features.Recruitment.DTOs;

// ============================================================================
// US-REC-008 candidate-portal DTOs (camelCase on the wire). EVERYTHING here is a
// SANITIZED, applicant-facing view (NFR-5/BR-3): NO rejection reasons, NO scorecard
// data, NO internal notes are ever projected into these shapes.
// ============================================================================

/// <summary>
/// The full candidate-portal dashboard for one applicant email in one tenant (US-REC-008 FR-2/AC-1). One
/// <see cref="PortalApplicationDto"/> per application the email submitted in this tenant. Tenant-scoped via
/// the magic-link token + the EF global query filter (AC-4).
/// </summary>
public sealed record PortalDashboardDto
{
    /// <summary>The applicant's display name (from the most recent application).</summary>
    public string ApplicantName { get; init; } = string.Empty;

    /// <summary>The applicant email this dashboard is scoped to.</summary>
    public string ApplicantEmail { get; init; } = string.Empty;

    /// <summary>The applications submitted by this email in this tenant (FR-2), newest first.</summary>
    public IReadOnlyList<PortalApplicationDto> Applications { get; init; } = [];
}

/// <summary>
/// One application as the candidate sees it (US-REC-008 FR-2/AC-1). Carries the vacancy summary, the current
/// stage + the ordered stage list for the step indicator, upcoming interview details (FR-3/AC-2), the active
/// offer summary (FR-2/AC-3), and the sanitized timeline (FR-6).
/// </summary>
public sealed record PortalApplicationDto
{
    public Guid ApplicantId { get; init; }
    public string ApplicationReferenceNumber { get; init; } = string.Empty;

    public Guid VacancyId { get; init; }
    public string VacancyTitle { get; init; } = string.Empty;
    public string? DepartmentName { get; init; }

    public DateTime AppliedAt { get; init; }

    /// <summary>Current pipeline stage (PascalCase enum on the wire, US-PLT-003).</summary>
    public ApplicantStage CurrentStage { get; init; }
    public string CurrentStageName { get; init; } = string.Empty;

    /// <summary>
    /// Ordered step-indicator stages for the visual progress tracker (AC-1): Applied → Screening →
    /// Interview → Offer → Hired, each flagged completed/current. A Rejected application surfaces a
    /// terminal "Rejected" step (no internal reason — BR-3).
    /// </summary>
    public IReadOnlyList<PortalStageStepDto> Steps { get; init; } = [];

    /// <summary>The next upcoming, scheduled interview for this application (FR-3/AC-2), or null.</summary>
    public PortalInterviewDto? UpcomingInterview { get; init; }

    /// <summary>The active (Sent) offer for this application (FR-2/AC-3), or null.</summary>
    public PortalOfferDto? ActiveOffer { get; init; }

    /// <summary>Sanitized status-change timeline, newest first (FR-6) — labels only, no internal notes.</summary>
    public IReadOnlyList<PortalTimelineEventDto> Timeline { get; init; } = [];
}

/// <summary>One step in the visual progress tracker (US-REC-008 AC-1 step indicator).</summary>
public sealed record PortalStageStepDto
{
    public ApplicantStage Stage { get; init; }
    public string StageName { get; init; } = string.Empty;

    /// <summary>Zero-based position in the pipeline order (left-to-right).</summary>
    public int Order { get; init; }

    /// <summary>True for stages the applicant has already passed.</summary>
    public bool IsCompleted { get; init; }

    /// <summary>True for the applicant's current stage.</summary>
    public bool IsCurrent { get; init; }
}

/// <summary>
/// Sanitized upcoming-interview details for the candidate (US-REC-008 FR-3/AC-2): date, time, type,
/// location/video link, and interviewer NAMES only (no scorecards or internal notes — NFR-5/BR-3).
/// </summary>
public sealed record PortalInterviewDto
{
    public Guid Id { get; init; }
    public int RoundNumber { get; init; }

    public InterviewType InterviewType { get; init; }
    public string InterviewTypeName { get; init; } = string.Empty;

    public DateOnly ScheduledDate { get; init; }
    public TimeOnly StartTime { get; init; }
    public int DurationMinutes { get; init; }

    public string? Location { get; init; }
    public string? VideoLink { get; init; }

    /// <summary>Interviewer display names only (FR-3) — no emails, ids, or scorecard data exposed.</summary>
    public IReadOnlyList<string> InterviewerNames { get; init; } = [];
}

/// <summary>
/// Sanitized active-offer summary for the candidate (US-REC-008 FR-2/AC-3): position, salary, start/expiry
/// dates, and whether the letter is downloadable. No internal fields are exposed.
/// </summary>
public sealed record PortalOfferDto
{
    public Guid Id { get; init; }
    public string OfferReferenceNumber { get; init; } = string.Empty;

    public OfferStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;

    public string OfferedPosition { get; init; } = string.Empty;
    public decimal SalaryAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public SalaryFrequency SalaryFrequency { get; init; }
    public string SalaryFrequencyName { get; init; } = string.Empty;

    public DateOnly StartDate { get; init; }
    public DateOnly ExpiryDate { get; init; }

    /// <summary>True when a generated PDF exists and can be downloaded via the portal (FR-4/AC-3).</summary>
    public bool IsDocumentAvailable { get; init; }

    /// <summary>True when the applicant can still accept/decline (offer is Sent, BR-2 one-time action).</summary>
    public bool CanRespond { get; init; }
}

/// <summary>
/// One sanitized timeline event (US-REC-008 FR-6) — e.g. "Moved to Screening". Carries the stage label and
/// when it happened; NEVER the internal reason, rejection reason, or notes (BR-3/NFR-5).
/// </summary>
public sealed record PortalTimelineEventDto
{
    /// <summary>A human-friendly, sanitized label, e.g. "Application received" / "Moved to Interview".</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The stage the application moved to.</summary>
    public ApplicantStage Stage { get; init; }
    public string StageName { get; init; } = string.Empty;

    public DateTime OccurredAt { get; init; }
}

/// <summary>
/// The generic, anti-enumeration response to a candidate's "request a new link" call (US-REC-008 BR-5).
/// Never reveals whether an application exists or echoes a token.
/// </summary>
public sealed record PortalLinkRequestResultDto
{
    public string Message { get; init; } = string.Empty;
}
