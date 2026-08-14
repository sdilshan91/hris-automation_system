using HRM.Domain.Enums;

namespace HRM.Application.Features.Performance.DTOs;

// ── Output DTOs ─────────────────────────────────────────────────────────

/// <summary>One improvement objective of a PIP (US-PRF-008 AC-1/FR-1/FR-6).</summary>
public sealed record PipObjectiveDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string SuccessCriteria { get; init; } = string.Empty;
    public DateOnly DueDate { get; init; }
    public int SortOrder { get; init; }
    public bool AddedAtExtension { get; init; }

    /// <summary>
    /// GAP-012 / ISSUE-373: the checkpoints recorded against THIS objective — the body of the per-objective
    /// accordion the UI has always rendered (<c>IPipObjective.checkpoints</c>).
    ///
    /// <para>Checkpoints attributed to the PIP as a whole (<c>ObjectiveId == null</c>, which is every row
    /// predating this change) do NOT appear here; they remain in the PIP-level <c>Checkpoints</c> list, which
    /// stays the complete set. So the two are not duplicates of each other and the flat list is still the one
    /// to count.</para>
    /// </summary>
    public IReadOnlyList<PipCheckpointDto> Checkpoints { get; init; } = [];
}

/// <summary>One recorded checkpoint assessment — append-only history (US-PRF-008 AC-3/FR-4/FR-5).</summary>
public sealed record PipCheckpointDto
{
    public Guid Id { get; init; }

    /// <summary>
    /// GAP-012 / ISSUE-373: the objective this checkpoint measures, or null when it was recorded against the
    /// PIP as a whole. Null is not a defect — it is what every checkpoint predating the relationship is.
    /// </summary>
    public Guid? ObjectiveId { get; init; }
    public DateOnly CheckpointDate { get; init; }
    public PipCheckpointStatus ProgressStatus { get; init; }
    public string ProgressStatusName { get; init; } = string.Empty;
    public string EvidenceNotes { get; init; } = string.Empty;
    public Guid? ReviewerEmployeeId { get; init; }
    public string ReviewerName { get; init; } = string.Empty;
    public DateTime RecordedAt { get; init; }
    public string? AttachmentFileName { get; init; }
    public long? AttachmentSizeBytes { get; init; }
}

/// <summary>One immutable PIP audit-log entry (US-PRF-008 FR-5).</summary>
public sealed record PipEventDto
{
    public Guid Id { get; init; }
    public PipEventType EventType { get; init; }
    public string EventTypeName { get; init; } = string.Empty;
    public string ActorName { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; }
    public string? Detail { get; init; }
}

/// <summary>
/// A full PIP record (US-PRF-008). Returned from create/get/update operations. The sensitive Reason /
/// EscalationNotes are included for authorized viewers only (the query handler already enforces FR-8 visibility
/// before this DTO is built). The append-only checkpoint + event lists ARE the immutable history (FR-5).
/// </summary>
public sealed record PipDto
{
    public Guid Id { get; init; }

    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;

    public Guid? ManagerEmployeeId { get; init; }
    public string? ManagerName { get; init; }
    public Guid? MentorEmployeeId { get; init; }
    public string? MentorName { get; init; }
    public Guid? OriginManagerReviewId { get; init; }

    public string Reason { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }

    public PipStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;

    public PipEscalationAction EscalationAction { get; init; }
    public string EscalationActionName { get; init; } = string.Empty;

    public DateTime? InitiatedAt { get; init; }

    public PipAcknowledgementStatus AcknowledgementStatus { get; init; }
    public string AcknowledgementStatusName { get; init; } = string.Empty;
    public DateTime? AcknowledgedAt { get; init; }

    public DateTime? OutcomeSetAt { get; init; }
    public string? FinalOutcomeNotes { get; init; }
    public bool EscalationConfirmed { get; init; }
    public DateTime? EscalationConfirmedAt { get; init; }
    public string? EscalationNotes { get; init; }

    public IReadOnlyList<PipObjectiveDto> Objectives { get; init; } = [];
    public IReadOnlyList<PipCheckpointDto> Checkpoints { get; init; } = [];
    public IReadOnlyList<PipEventDto> Events { get; init; } = [];
}

/// <summary>
/// Pre-fill payload for the create-PIP form (US-PRF-008 AC-1). Seeds the HR officer's New-PIP form: the target
/// employee (name + job title + manager) when arriving from a flagged review or a directory row, a suggested
/// reason derived from the origin manager review (US-PRF-003) when one is supplied, the configured escalation
/// actions to offer (BR-6), and a client-side BR-2 pre-check (<see cref="HasActivePip"/>) so the form can warn
/// before submit — the create path still re-enforces BR-2 server-side. All fields are null/empty for a blank
/// HR-initiated form (no employeeId).
/// </summary>
public sealed record PipDraftDto
{
    /// <summary>The target employee id — null for a blank HR-initiated form.</summary>
    public Guid? EmployeeId { get; init; }

    /// <summary>The target employee's display name — null for a blank form.</summary>
    public string? EmployeeName { get; init; }

    /// <summary>The target employee's job title — null for a blank form / when unresolved.</summary>
    public string? JobTitle { get; init; }

    /// <summary>The target employee's current manager display name — null when none / blank form.</summary>
    public string? ManagerName { get; init; }

    /// <summary>Suggested PIP reason seeded from the flagged origin manager review; null when none supplied.</summary>
    public string? SuggestedReason { get; init; }

    /// <summary>BR-2 pre-check: true if the employee already has a non-terminal PIP (blocks creation).</summary>
    public bool HasActivePip { get; init; }

    /// <summary>The escalation actions offered to HR (BR-6) — every configurable action (excludes None).</summary>
    public IReadOnlyList<PipEscalationAction> EscalationOptions { get; init; } = [];
}

/// <summary>A summary row for the PIP list view (US-PRF-008 AC-1 dedicated PIP section, BR-5).</summary>
public sealed record PipSummaryDto
{
    public Guid Id { get; init; }
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string EmployeeNo { get; init; } = string.Empty;
    public PipStatus Status { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public PipAcknowledgementStatus AcknowledgementStatus { get; init; }
    public string AcknowledgementStatusName { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public string? MentorName { get; init; }
    public int ObjectiveCount { get; init; }
    public int CheckpointCount { get; init; }
}

// ── Request bodies (controller) ─────────────────────────────────────────

/// <summary>One objective in a create/extend request (US-PRF-008 AC-1/FR-1/FR-6).</summary>
public sealed record PipObjectiveInput(
    string Title,
    string? Description,
    string SuccessCriteria,
    DateOnly DueDate);

/// <summary>
/// Create-PIP request (US-PRF-008 AC-1/AC-2/FR-1). HR-only (BR-1). The employee, manager and mentor are referenced
/// by id; tenant is resolved server-side (never sent). Checkpoint dates schedule the Hangfire reminders (FR-3).
/// </summary>
public sealed record CreatePipRequest(
    Guid EmployeeId,
    string Reason,
    DateOnly StartDate,
    DateOnly EndDate,
    Guid? MentorEmployeeId,
    Guid? OriginManagerReviewId,
    PipEscalationAction EscalationAction,
    IReadOnlyList<PipObjectiveInput> Objectives,
    IReadOnlyList<DateOnly> CheckpointDates);

/// <summary>Record-checkpoint request (US-PRF-008 AC-3/FR-4). Manager or HR. File metadata optional.</summary>
public sealed record RecordCheckpointRequest(
    DateOnly CheckpointDate,
    PipCheckpointStatus ProgressStatus,
    string EvidenceNotes,
    string? AttachmentFileName,
    string? AttachmentStorageKey,
    string? AttachmentContentType,
    long? AttachmentSizeBytes);

/// <summary>The outcome kind for the set-outcome request (US-PRF-008 AC-4).</summary>
public enum PipOutcomeKind
{
    SuccessfullyCompleted = 0,
    Extended = 1,
    NotMet = 2,
}

/// <summary>
/// Set-final-outcome request (US-PRF-008 AC-4). HR-only (BR-1). For <see cref="PipOutcomeKind.Extended"/> a new
/// end date is required and optional new objectives may be added (FR-6). Notes are recorded on the PIP/event log.
/// </summary>
public sealed record SetPipOutcomeRequest(
    PipOutcomeKind Outcome,
    string? Notes,
    DateOnly? NewEndDate,
    IReadOnlyList<PipObjectiveInput>? NewObjectives);

/// <summary>Confirm-escalation request (US-PRF-008 AC-5/BR-6). HR-only. Records the escalation decision + notes.</summary>
public sealed record ConfirmEscalationRequest(
    PipEscalationAction EscalationAction,
    string? Notes);

// ── Service-layer inputs (decouple handlers from the service) ───────────

public sealed record CreatePipInput(
    Guid EmployeeId,
    string Reason,
    DateOnly StartDate,
    DateOnly EndDate,
    Guid? MentorEmployeeId,
    Guid? OriginManagerReviewId,
    PipEscalationAction EscalationAction,
    IReadOnlyList<PipObjectiveInput> Objectives,
    IReadOnlyList<DateOnly> CheckpointDates,
    string? ClientIpAddress);

public sealed record RecordCheckpointInput(
    Guid PipId,
    /// <summary>
    /// GAP-012 / ISSUE-373: the objective this checkpoint measures. OPTIONAL and defaulted to null so every
    /// existing caller keeps compiling and keeps its current behaviour — a checkpoint recorded against the PIP
    /// as a whole. Placed last in the parameter list for the same reason.
    /// </summary>
    DateOnly CheckpointDate,
    PipCheckpointStatus ProgressStatus,
    string EvidenceNotes,
    string? AttachmentFileName,
    string? AttachmentStorageKey,
    string? AttachmentContentType,
    long? AttachmentSizeBytes,
    string? ClientIpAddress,
    Guid? ObjectiveId = null);

public sealed record SetPipOutcomeInput(
    Guid PipId,
    PipOutcomeKind Outcome,
    string? Notes,
    DateOnly? NewEndDate,
    IReadOnlyList<PipObjectiveInput>? NewObjectives,
    string? ClientIpAddress);

public sealed record ConfirmEscalationInput(
    Guid PipId,
    PipEscalationAction EscalationAction,
    string? Notes,
    string? ClientIpAddress);

public sealed record AcknowledgePipInput(
    Guid PipId,
    string? ClientIpAddress);

/// <summary>
/// Create-form pre-fill request (US-PRF-008 AC-1). <paramref name="EmployeeId"/> is optional — null opens a blank
/// HR-initiated form. <paramref name="ReviewId"/> is the optional flagged origin manager review to seed the reason.
/// </summary>
public sealed record GetPipDraftInput(
    Guid? EmployeeId,
    Guid? ReviewId);
