using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// One agreed development action with a deadline within a <see cref="ReviewMeetingNotes"/> (US-PRF-006 FR-2).
/// Plain-text description (not HTML) + an optional due date. Tenant-scoped via <see cref="BaseEntity.TenantId"/>
/// + the EF global query filter + <c>TenantInterceptor</c> (NFR-2). Maps to the "review_meeting_notes_action"
/// table.
/// </summary>
public sealed class ReviewMeetingNotesAction : BaseEntity
{
    /// <summary>The parent meeting-notes row (FK, required).</summary>
    public Guid ReviewMeetingNotesId { get; set; }

    /// <summary>The agreed action, plain text (required, max 1000 chars).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional agreed deadline for the action (FR-2).</summary>
    public DateOnly? Deadline { get; set; }

    /// <summary>Stable display order within the notes (0-based).</summary>
    public int SortOrder { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public ReviewMeetingNotes? ReviewMeetingNotes { get; set; }
}
