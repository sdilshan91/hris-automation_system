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

    /// <summary>
    /// GAP-012 / ISSUE-373: the objective this checkpoint measures progress against. NULLABLE — a checkpoint
    /// may still be recorded against the PIP as a whole.
    ///
    /// <para><b>Why this was added.</b> The Angular UI has always rendered checkpoints as the body of a
    /// per-objective accordion (<c>IPipObjective.checkpoints</c>), while the model attached them only to the
    /// PIP. Neither US-PRF-010 nor the tech doc documents either design, so there was no authority to appeal
    /// to — the UI was expressing something the schema could not represent. A checkpoint that measures
    /// progress is more useful attached to the objective it measures, so the model gains the relationship
    /// rather than the UI losing the grouping.</para>
    ///
    /// <para><b>Why nullable rather than required.</b> Existing checkpoints have no objective to attribute
    /// them to, and guessing one would fabricate history — a PIP may have several objectives, so there is no
    /// unambiguous backfill. Null therefore means "recorded against the PIP as a whole", which is exactly what
    /// every pre-existing row genuinely is. It also keeps the FK optional, avoiding the required-navigation
    /// INNER JOIN trap that made employees vanish when their job title was soft-deleted (see
    /// <c>ManagerReviewService.ResolveJobTitleAsync</c>).</para>
    /// </summary>
    public Guid? ObjectiveId { get; set; }

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

    /// <summary>The objective this checkpoint measures, when it is attributed to one. See <see cref="ObjectiveId"/>.</summary>
    public PipObjective? Objective { get; set; }
}
