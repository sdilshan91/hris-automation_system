using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// An IMMUTABLE, append-only progress update an employee posts against one of their <see cref="Goal"/>s during
/// the active cycle period (US-PRF-009 AC-2/FR-1/FR-2/NFR-3). Each update captures the progress percentage
/// (0-100), the chosen <see cref="GoalProgressStatus"/>, free-text notes (≤2000 chars) and optional evidence
/// attachment metadata (≤3 files). Rows are NEVER updated or deleted — there is no update path and no concurrency
/// token — so the ordered set of rows IS the goal's progress timeline (FR-3) and a tamper-proof record for the
/// formal review (NFR-3). The goal's CURRENT progress/status is simply the LATEST update by <see cref="CreatedAt"/>.
/// Directly mirrors the US-PRF-006 ReviewSignoff / US-PRF-008 PipEvent immutable-log pattern. Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c> (NFR-2). Maps to the
/// "goal_progress_update" table.
///
/// ATTACHMENT SEAM (FR-2): the attachment files are referenced by metadata (file name, storage key, content type,
/// size) only — the blob lives in the platform's tenant-scoped file storage, uploaded out-of-band, exactly as the
/// US-PRF-008 PipCheckpoint attachment fields work. Up to 3 attachments per update (each ≤10MB); the metadata is
/// stored as the owned <see cref="GoalProgressAttachment"/> collection.
/// </summary>
public sealed class GoalProgressUpdate : BaseEntity
{
    /// <summary>The goal this update belongs to (FK to <see cref="Goal"/>, required).</summary>
    public Guid GoalId { get; set; }

    /// <summary>The employee who posted the update (FK to <see cref="Employee"/>, required — the goal's owner).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Progress percentage at this update, 0-100 (FR-2).</summary>
    public int ProgressPct { get; set; }

    /// <summary>The status chosen for this update (FR-2/FR-7). BR-2 auto-sets Completed at 100% (overridable).</summary>
    public GoalProgressStatus Status { get; set; } = GoalProgressStatus.NotStarted;

    /// <summary>Update notes / evidence narrative (FR-2, max 2000 chars). Optional.</summary>
    public string? Notes { get; set; }

    /// <summary>UTC timestamp the update was posted (FR-3 timeline ordering). Set on create; never changes.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // ── Navigation ─────────────────────────────────────────────────────
    public Goal? Goal { get; set; }

    /// <summary>The optional evidence attachment metadata (≤3, FR-2). Owned + immutable with the update.</summary>
    public List<GoalProgressAttachment> Attachments { get; set; } = [];
}
