using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// Evidence-attachment metadata for a <see cref="GoalProgressUpdate"/> (US-PRF-009 FR-2/AC-2). Up to 3 per update,
/// each ≤10MB. The blob itself lives in the platform's tenant-scoped file storage (uploaded out-of-band); this row
/// only carries the metadata + storage key, mirroring the US-PRF-008 PipCheckpoint attachment-seam approach. Like
/// its parent update it is append-only (created with the update, never edited or deleted in isolation). Tenant-scoped
/// via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c> (NFR-2). Maps to
/// the "goal_progress_attachment" table.
/// </summary>
public sealed class GoalProgressAttachment : BaseEntity
{
    /// <summary>The progress update this attachment belongs to (FK, required).</summary>
    public Guid ProgressUpdateId { get; set; }

    /// <summary>The original file name (max 512 chars, FR-2).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>The opaque tenant-scoped storage key the blob was uploaded under (max 1024 chars).</summary>
    public string StorageKey { get; set; } = string.Empty;

    /// <summary>The file MIME content type (max 255 chars).</summary>
    public string? ContentType { get; set; }

    /// <summary>The file size in bytes (≤10MB, FR-2 — validated at the API layer).</summary>
    public long SizeBytes { get; set; }

    // ── Navigation ─────────────────────────────────────────────────────
    public GoalProgressUpdate? ProgressUpdate { get; set; }
}
