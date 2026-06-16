using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// One recorded checkpoint assessment of a <see cref="Pip"/> (US-PRF-008 AC-3/FR-4). Recording a checkpoint is
/// append-only history (FR-5): once written a row is NEVER updated or deleted (the service exposes no edit path),
/// so the ordered set of checkpoints is the immutable progress trail. Captures the checkpoint date, the progress
/// status (traffic light), evidence notes, the reviewer who recorded it, the recorded-at timestamp, and optional
/// file-attachment metadata (the file itself lives in storage; only metadata is referenced here, mirroring
/// <c>SelfAssessmentAttachment</c>). Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query
/// filter + <c>TenantInterceptor</c> (NFR-2). Maps to the "pip_checkpoint" table.
/// </summary>
public sealed class PipCheckpoint : BaseEntity
{
    /// <summary>The parent PIP (FK, required).</summary>
    public Guid PipId { get; set; }

    /// <summary>The scheduled/actual checkpoint date (AC-3).</summary>
    public DateOnly CheckpointDate { get; set; }

    /// <summary>The progress assessment recorded by the reviewer (AC-3 — on track / at risk / not met).</summary>
    public PipCheckpointStatus ProgressStatus { get; set; }

    /// <summary>Evidence of improvement / progress assessment notes (max 4000 chars, AC-3/FR-4).</summary>
    public string EvidenceNotes { get; set; } = string.Empty;

    /// <summary>The reviewer's employee id (manager or HR who recorded the checkpoint, AC-3).</summary>
    public Guid? ReviewerEmployeeId { get; set; }

    /// <summary>The reviewer's display name captured at record time (audit/FR-5).</summary>
    public string ReviewerName { get; set; } = string.Empty;

    /// <summary>UTC timestamp the checkpoint was recorded (AC-3/FR-5 immutable).</summary>
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    // ── Optional file-attachment metadata (FR-4) ────────────────────────

    /// <summary>The stored file key/path for an evidence attachment (FR-4). Null when no file attached.</summary>
    public string? AttachmentStorageKey { get; set; }

    /// <summary>The original file name of the evidence attachment (FR-4). Null when no file attached.</summary>
    public string? AttachmentFileName { get; set; }

    /// <summary>The content type of the evidence attachment (FR-4). Null when no file attached.</summary>
    public string? AttachmentContentType { get; set; }

    /// <summary>The size in bytes of the evidence attachment (FR-4). Null when no file attached.</summary>
    public long? AttachmentSizeBytes { get; set; }

    // ── Navigation ─────────────────────────────────────────────────────
    public Pip? Pip { get; set; }
}
