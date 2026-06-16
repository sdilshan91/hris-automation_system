using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// The performance-review meeting notes for one <see cref="ManagerReview"/> (US-PRF-006 FR-1/FR-2). Exactly
/// one per review (tenant-scoped unique index). The rich-text <see cref="Body"/> is stored as SANITIZED HTML
/// (FR-1, §10 — sanitized on write to prevent XSS); the structured sections (key strengths, development
/// areas, overall summary) are distinct sanitized-HTML fields, and the agreed development actions with
/// deadlines (FR-2) live in the child <see cref="ReviewMeetingNotesAction"/> rows. Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c> (NFR-2). Maps to
/// the "review_meeting_notes" table.
/// </summary>
public sealed class ReviewMeetingNotes : BaseEntity
{
    /// <summary>The manager review these notes attach to (FK, required; one-to-one).</summary>
    public Guid ManagerReviewId { get; set; }

    /// <summary>
    /// Free-form discussion body / overall narrative as SANITIZED HTML (FR-1). The editor pre-populates this
    /// from a template (FR-1/FR-2). Never store unsanitized HTML — the service sanitizes on every write.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>Key strengths section as SANITIZED HTML (FR-2).</summary>
    public string? Strengths { get; set; }

    /// <summary>Development / areas-for-improvement section as SANITIZED HTML (FR-2).</summary>
    public string? DevelopmentAreas { get; set; }

    /// <summary>Overall discussion summary section as SANITIZED HTML (FR-2).</summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Optimistic concurrency token (mirrors <c>ManagerReview.Version</c>). Maps to the PostgreSQL xmin system
    /// column via the Npgsql row-version convention (no schema DDL); the InMemory test provider ignores it.
    /// </summary>
    public uint Version { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public ManagerReview? ManagerReview { get; set; }

    /// <summary>Agreed development actions with deadlines (FR-2).</summary>
    public List<ReviewMeetingNotesAction> Actions { get; set; } = [];
}
