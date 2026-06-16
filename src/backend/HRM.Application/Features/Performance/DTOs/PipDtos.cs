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
}

/// <summary>One recorded checkpoint assessment — append-only history (US-PRF-008 AC-3/FR-4/FR-5).</summary>
public sealed record PipCheckpointDto
{
    public Guid Id { get; init; }
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
    DateOnly CheckpointDate,
    PipCheckpointStatus ProgressStatus,
    string EvidenceNotes,
    string? AttachmentFileName,
    string? AttachmentStorageKey,
    string? AttachmentContentType,
    long? AttachmentSizeBytes,
    string? ClientIpAddress);

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
