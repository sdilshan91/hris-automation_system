using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// One manager rating row within a <see cref="ManagerReview"/> — the manager's evaluation of a single goal
/// (US-PRF-003 FR-2/FR-3). Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter
/// + <c>TenantInterceptor</c>. Maps to the "manager_review_item" table.
/// </summary>
public sealed class ManagerReviewItem : BaseEntity
{
    /// <summary>The parent manager review (FK, required).</summary>
    public Guid ManagerReviewId { get; set; }

    /// <summary>The goal being rated (FK to <see cref="Goal"/>, required).</summary>
    public Guid GoalId { get; set; }

    /// <summary>
    /// Manager rating on the cycle's configured scale (FR-2, same scale as the self-assessment). Integer in
    /// [1, cycle.RatingScaleMax]. Null while a draft is partially filled; required (non-null) before submit (AC-3).
    /// </summary>
    public int? ManagerRating { get; set; }

    /// <summary>
    /// Manager comment for this goal (FR-3). Required at submit with a minimum of 20 characters; may be
    /// null/short in a draft. Stored as plain text.
    /// </summary>
    public string? ManagerComment { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public ManagerReview? ManagerReview { get; set; }
    public Goal? Goal { get; set; }
}
