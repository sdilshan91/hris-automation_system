using HRM.Domain.Enums;

namespace HRM.Application.Features.Performance.DTOs;

// ── Meeting notes (AC-1/FR-1/FR-2) ──────────────────────────────────────

/// <summary>One agreed development action with a deadline (US-PRF-006 FR-2).</summary>
public sealed record MeetingNotesActionDto(Guid Id, string Description, DateOnly? Deadline, int SortOrder);

/// <summary>
/// The meeting notes + sign-off state for one manager review (US-PRF-006 AC-1/AC-2/AC-3). Rich-text fields
/// are SANITIZED HTML (FR-1). Used by the editor/view and returned from add-notes / request / sign / dispute
/// / resolve operations.
/// </summary>
public sealed record ReviewMeetingNotesDto
{
    public Guid ManagerReviewId { get; init; }
    public Guid? MeetingNotesId { get; init; }

    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;
    public Guid CycleId { get; init; }

    /// <summary>
    /// ISSUE-379: the US-PRF-006 sign-off screen renders the cycle name, the rating scale and the final
    /// score. None were on this payload, so that surface blanked.
    ///
    /// <para>These are EXPOSURE, not new work: <c>BuildNotesDtoFrom</c> already receives the
    /// <see cref="AppraisalCycle"/> and <see cref="ManagerReview"/> they come from, and the export path
    /// 250 lines away already reads the identical expressions
    /// (<c>ReviewSignoffService.cs:400,409,412</c>). Zero additional queries.</para>
    /// </summary>
    public string CycleName { get; init; } = string.Empty;

    /// <summary>Denominator for the score display (e.g. 5). Read off the cycle, like the export path.</summary>
    public int RatingScaleMax { get; init; }

    /// <summary>Nullable by design: a review that has not been scored yet has no final score.</summary>
    public decimal? FinalScore { get; init; }

    /// <summary>Sanitized-HTML discussion body (FR-1). When notes do not exist yet, the template default.</summary>
    public string? Body { get; init; }
    public string? Strengths { get; init; }
    public string? DevelopmentAreas { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<MeetingNotesActionDto> Actions { get; init; } = [];

    /// <summary>Sign-off lifecycle status (AC-2/AC-3/BR-3/BR-4).</summary>
    public ReviewSignoffStatus SignoffStatus { get; init; }
    public string SignoffStatusName { get; init; } = string.Empty;

    public DateTime? SignoffRequestedAt { get; init; }
    public DateTime? SignoffCompletedAt { get; init; }

    /// <summary>BR-2: when the reviewed employee first opened these notes. The FE enables Acknowledge &amp; Sign
    /// only once this is set (read-before-sign). Null until read.</summary>
    public DateTime? NotesOpenedAt { get; init; }

    /// <summary>True once locked by employee sign-off (BR-5) — the FE renders read-only.</summary>
    public bool IsLocked { get; init; }

    /// <summary>True when these are template defaults (no notes row persisted yet).</summary>
    public bool IsTemplate { get; init; }

    /// <summary>The immutable sign-off log, oldest first (FR-7).</summary>
    public IReadOnlyList<ReviewSignoffEntryDto> Signoffs { get; init; } = [];
}

/// <summary>One immutable sign-off log entry (US-PRF-006 FR-3/FR-7).</summary>
public sealed record ReviewSignoffEntryDto
{
    public Guid Id { get; init; }
    public SignoffParty Party { get; init; }
    public string PartyName { get; init; } = string.Empty;
    public SignoffAction Action { get; init; }
    public string ActionName { get; init; } = string.Empty;
    public string SignerName { get; init; } = string.Empty;
    public DateTime SignedAt { get; init; }
    public string? ClientIpAddress { get; init; }
    public string? Comments { get; init; }
}

// ── Complete review export record (AC-4/FR-6) ───────────────────────────

/// <summary>
/// The complete review record for export (US-PRF-006 AC-4/FR-6): goals, ratings, meeting notes, sign-off
/// timestamps and the full immutable signature log. Exposed as DATA; PDF rendering is a deliberate seam (the
/// codebase has no PDF library — see US-PRF-005). Tenant logo/branding (FR-6) is to be applied at render time.
/// </summary>
public sealed record ReviewExportDto
{
    public Guid ManagerReviewId { get; init; }
    public Guid CycleId { get; init; }
    public string CycleName { get; init; } = string.Empty;

    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;

    public string? ReviewerName { get; init; }

    public ManagerReviewStatus ReviewStatus { get; init; }
    public string ReviewStatusName { get; init; } = string.Empty;
    public ReviewSignoffStatus SignoffStatus { get; init; }
    public string SignoffStatusName { get; init; } = string.Empty;

    public int RatingScaleMax { get; init; }
    public decimal? WeightedSelfScore { get; init; }
    public decimal? WeightedManagerScore { get; init; }
    public decimal? FinalScore { get; init; }
    public string? SummaryComment { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public DateTime? SignoffCompletedAt { get; init; }

    /// <summary>BR-2: when the reviewed employee opened the notes prior to signature (read-before-sign). Null if never opened.</summary>
    public DateTime? NotesOpenedAt { get; init; }

    public bool IsLocked { get; init; }

    public IReadOnlyList<ReviewExportGoalDto> Goals { get; init; } = [];

    /// <summary>Meeting notes (null if none were recorded).</summary>
    public ReviewMeetingNotesDto? MeetingNotes { get; init; }

    /// <summary>The immutable signature log (FR-3/FR-7), oldest first.</summary>
    public IReadOnlyList<ReviewSignoffEntryDto> Signoffs { get; init; } = [];
}

/// <summary>One goal + its self/manager ratings in the export record (US-PRF-006 AC-4).</summary>
public sealed record ReviewExportGoalDto
{
    public Guid GoalId { get; init; }
    public string GoalTitle { get; init; } = string.Empty;
    public int GoalWeight { get; init; }
    public int? SelfRating { get; init; }
    public string? SelfComment { get; init; }
    public int? ManagerRating { get; init; }
    public string? ManagerComment { get; init; }
}

// ── Request bodies ──────────────────────────────────────────────────────

/// <summary>One agreed action in a save-notes request (US-PRF-006 FR-2).</summary>
public sealed record MeetingNotesActionInput(string Description, DateOnly? Deadline);

/// <summary>
/// Save / request-sign-off body for meeting notes (US-PRF-006 AC-1/AC-2). The rich-text fields are sanitized
/// server-side before persistence (FR-1, §10). The manager + tenant are resolved from the caller (never sent).
/// </summary>
public sealed record SaveMeetingNotesRequest(
    string? Body,
    string? Strengths,
    string? DevelopmentAreas,
    string? Summary,
    IReadOnlyList<MeetingNotesActionInput>? Actions);

/// <summary>Employee dispute body (US-PRF-006 AC-3/FR-4/FR-5) — comments are mandatory.</summary>
public sealed record DisputeReviewRequest(string Comments);

/// <summary>HR dispute-resolution body (US-PRF-006 BR-4): confirm as-is or amend, with resolution notes.</summary>
public sealed record ResolveDisputeRequest(bool Amend, string? Comments);

// ── Service-layer inputs (decouple handlers from the service) ───────────

public sealed record SaveMeetingNotesInput(
    Guid CycleId,
    Guid EmployeeId,
    string? Body,
    string? Strengths,
    string? DevelopmentAreas,
    string? Summary,
    IReadOnlyList<MeetingNotesActionInput> Actions);

/// <summary>An employee sign-off / dispute action with the request context the service persists (FR-7).</summary>
public sealed record SignoffActionInput(
    Guid CycleId,
    Guid EmployeeId,
    string? Comments,
    string? ClientIpAddress);

public sealed record ResolveDisputeInput(
    Guid CycleId,
    Guid EmployeeId,
    bool Amend,
    string? Comments,
    string? ClientIpAddress);
