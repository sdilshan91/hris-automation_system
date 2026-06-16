using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// A lightweight manager (or HR) comment on a goal's progress, forming a conversation thread per goal
/// (US-PRF-009 FR-8/AC-3). A comment is optionally anchored to a specific <see cref="GoalProgressUpdate"/>
/// (<see cref="ProgressUpdateId"/>) so the FE can render it inline under that timeline entry; when null it is a
/// goal-level comment. Append-only — comments accumulate and are not edited/deleted in this story (keeps the
/// thread a faithful record, consistent with the module's immutable-history posture). The body is capped at 500
/// chars to encourage quick feedback (§10). Visibility follows the goal (BR-5: employee / manager / HR only).
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>
/// (NFR-2). Maps to the "goal_comment" table.
/// </summary>
public sealed class GoalComment : BaseEntity
{
    /// <summary>The goal this comment belongs to (FK to <see cref="Goal"/>, required).</summary>
    public Guid GoalId { get; set; }

    /// <summary>
    /// The progress update this comment is anchored to (FK to <see cref="GoalProgressUpdate"/>). Null for a
    /// goal-level comment not tied to a specific update.
    /// </summary>
    public Guid? ProgressUpdateId { get; set; }

    /// <summary>The comment author's employee id (FK to <see cref="Employee"/> — the manager or HR user, required).</summary>
    public Guid AuthorEmployeeId { get; set; }

    /// <summary>The author's full name captured at comment time (display).</summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>The comment body (max 500 chars, FR-8/§10).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>UTC timestamp the comment was posted (thread ordering). Set on create; never changes.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // ── Navigation ─────────────────────────────────────────────────────
    public Goal? Goal { get; set; }
    public GoalProgressUpdate? ProgressUpdate { get; set; }
}
