using HRM.Domain.Enums;

namespace HRM.Application.Features.Recruitment.DTOs;

/// <summary>
/// Kanban board for a vacancy's applicant pipeline (US-REC-003 AC-1/FR-1/FR-5). One
/// <see cref="PipelineStageColumnDto"/> per pipeline stage, ordered left-to-right by stage sequence,
/// each carrying its ordered applicant cards and a per-stage count, plus an overall total. All data is
/// tenant-scoped (AC-5).
/// </summary>
public sealed record ApplicantPipelineBoardDto
{
    public Guid VacancyId { get; init; }
    public string? VacancyTitle { get; init; }

    /// <summary>Stage columns in pipeline order (Applied → Screening → Interview → Offer → Hired → Rejected).</summary>
    public IReadOnlyList<PipelineStageColumnDto> Stages { get; init; } = [];

    /// <summary>Total applicants on the board after filters are applied (FR-5).</summary>
    public int Total { get; init; }
}

/// <summary>A single Kanban column: one pipeline stage with its ordered cards and count (FR-5).</summary>
public sealed record PipelineStageColumnDto
{
    public ApplicantStage Stage { get; init; }
    public string StageName { get; init; } = string.Empty;

    /// <summary>Zero-based position of this stage in the pipeline (left-to-right order, FR-1).</summary>
    public int Order { get; init; }

    /// <summary>Number of applicant cards in this column after filters (FR-5).</summary>
    public int Count { get; init; }

    /// <summary>Ordered applicant summary cards in this column (newest application first).</summary>
    public IReadOnlyList<PipelineApplicantCardDto> Applicants { get; init; } = [];
}

/// <summary>
/// Compact applicant card for the Kanban board (US-REC-003 FR-2): name, applied date, source, and
/// whether it is an internal application. The resume blob and cover-letter body are intentionally
/// omitted from the card payload.
/// </summary>
public sealed record PipelineApplicantCardDto
{
    public Guid Id { get; init; }
    public string ApplicationReferenceNumber { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public ApplicationSource Source { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public bool IsInternal { get; init; }
    public DateTime AppliedAt { get; init; }
}

/// <summary>
/// Full applicant detail for the slide-over panel (US-REC-003 AC-3/FR-7). Wraps the existing
/// <see cref="ApplicantDto"/> profile with the stage-transition timeline (BR-5) and a resume download
/// reference. Interview scores/schedules are deferred to US-REC-005/006 (no interview module yet) and
/// returned empty; signed resume URLs are deferred (local-dev file storage stub — the FE downloads via
/// the documented route instead).
/// </summary>
public sealed record ApplicantDetailDto
{
    public ApplicantDto Profile { get; init; } = default!;

    /// <summary>Stage-transition history, newest first (BR-5/AC-3 timeline).</summary>
    public IReadOnlyList<ApplicantStageHistoryDto> StageHistory { get; init; } = [];

    /// <summary>Original resume file name for display (no raw blob path is exposed — NFR-5).</summary>
    public string ResumeFileName { get; init; } = string.Empty;

    /// <summary>
    /// Relative API route the FE calls to download the resume. A short-lived signed URL is deferred
    /// (the file-storage backend is a local-dev stub); the FE requests this authenticated route instead.
    /// </summary>
    public string ResumeDownloadUrl { get; init; } = string.Empty;

    /// <summary>
    /// Interview schedules/scores — DEFERRED to US-REC-005/006 (no interview module yet). Always empty
    /// here; the field exists so the FE detail contract is stable.
    /// </summary>
    public IReadOnlyList<object> Interviews { get; init; } = [];
}

/// <summary>A single stage-transition timeline entry for the applicant detail panel (BR-5/AC-3).</summary>
public sealed record ApplicantStageHistoryDto
{
    public Guid Id { get; init; }
    public ApplicantStage FromStage { get; init; }
    public string FromStageName { get; init; } = string.Empty;
    public ApplicantStage ToStage { get; init; }
    public string ToStageName { get; init; } = string.Empty;
    public Guid? ChangedByUserId { get; init; }
    public string? Reason { get; init; }

    /// <summary>Structured rejection reason (US-REC-004 AC-4) — set only on a move to Rejected.</summary>
    public RejectionReason? RejectionReason { get; init; }

    /// <summary>String name of <see cref="RejectionReason"/> for display (null when not a rejection).</summary>
    public string? RejectionReasonName { get; init; }

    public string? Notes { get; init; }
    public DateTime ChangedAt { get; init; }
}

/// <summary>
/// Filter parameters for the pipeline board query (US-REC-003 FR-6/AC-4): optional single-stage filter,
/// application source, applied-date range, and a free-text search over name/email. All optional; an
/// empty filter returns the whole board.
/// </summary>
public sealed record PipelineFilter
{
    public ApplicantStage? Stage { get; init; }
    public ApplicationSource? Source { get; init; }
    public DateTime? AppliedFrom { get; init; }
    public DateTime? AppliedTo { get; init; }
    public string? Search { get; init; }
}

// ── Move-stage request body + result (camelCase for the FE) ────────────────────────────────────────

/// <summary>
/// Request body for moving a single applicant to a new stage (US-REC-003 AC-2/FR-3). A reason is
/// mandatory when <see cref="ToStage"/> is Rejected (BR-3) or when the move is backward (BR-4).
/// </summary>
public sealed record MoveApplicantStageRequest
{
    public ApplicantStage ToStage { get; init; }
    public string? Reason { get; init; }
    public string? Notes { get; init; }

    /// <summary>
    /// Structured rejection reason (US-REC-004 AC-4/FR-3). REQUIRED when <see cref="ToStage"/> is
    /// Rejected; ignored for any other target stage.
    /// </summary>
    public RejectionReason? RejectionReason { get; init; }

    /// <summary>
    /// Optimistic concurrency token (ISSUE-109): the applicant-detail xmin the client read. Echoed back
    /// to guard the stage move; a stale value yields 409 Conflict. Mirrors the employee-profile PATCH.
    /// </summary>
    public uint RowVersion { get; init; }
}

/// <summary>
/// Request body for moving several applicants to the same stage in one action (US-REC-003 FR-8 bulk
/// move). Same reason rules as the single move (BR-3/BR-4).
/// </summary>
public sealed record BulkMoveApplicantStageRequest
{
    public IReadOnlyList<Guid> ApplicantIds { get; init; } = [];
    public ApplicantStage ToStage { get; init; }
    public string? Reason { get; init; }
    public string? Notes { get; init; }

    /// <summary>
    /// Structured rejection reason (US-REC-004 AC-4/FR-3). REQUIRED when <see cref="ToStage"/> is
    /// Rejected; ignored for any other target stage.
    /// </summary>
    public RejectionReason? RejectionReason { get; init; }
}

/// <summary>Outcome of a single stage move (AC-2) — the new stage plus the history row id created.</summary>
public sealed record MoveApplicantStageResultDto
{
    public Guid ApplicantId { get; init; }
    public ApplicantStage FromStage { get; init; }
    public string FromStageName { get; init; } = string.Empty;
    public ApplicantStage ToStage { get; init; }
    public string ToStageName { get; init; } = string.Empty;
    public Guid StageHistoryId { get; init; }
    public DateTime ChangedAt { get; init; }

    /// <summary>Structured rejection reason recorded for this move (US-REC-004 AC-4) — null unless Rejected.</summary>
    public RejectionReason? RejectionReason { get; init; }

    /// <summary>
    /// Soft, overridable warnings surfaced by this move (US-REC-004): headcount-filled (BR-4) and the
    /// stubbed interview/scorecard gate framework (FR-1/BR-1, pending US-REC-005/006). The move STILL
    /// succeeded — these are advisory only, never a hard block. Empty when there is nothing to flag.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Outcome of a bulk stage move (FR-8) — per-applicant results plus a moved count.</summary>
public sealed record BulkMoveApplicantStageResultDto
{
    public int MovedCount { get; init; }
    public IReadOnlyList<MoveApplicantStageResultDto> Results { get; init; } = [];
}

/// <summary>
/// Applicant resume bytes streamed through the API (US-REC-003 FR-7/NFR-5) rather than exposing a
/// direct blob-storage URL. The controller returns these as a File response with a Content-Disposition
/// filename.
/// </summary>
public sealed record ResumeDownloadDto
{
    public required byte[] Content { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
}
