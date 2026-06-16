using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// One per-competency (or per-goal) rating row within a <see cref="Feedback360"/> (US-PRF-005 FR-4). The
/// reviewer rates a single competency/goal on the cycle's tenant-configured rating scale, with an optional
/// free-text comment. Aggregation (AC-4/FR-6) averages these per competency and per category. A row keys on
/// EITHER a goal id (when the competency is one of the employee's cycle goals) or a free-text competency key
/// (when the tenant configures named competencies on the rating scale) — exactly one of the two is set.
/// Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c>.
/// Maps to the "feedback_360_item" table.
/// </summary>
public sealed class Feedback360Item : BaseEntity
{
    /// <summary>The parent feedback record (FK, required).</summary>
    public Guid Feedback360Id { get; set; }

    /// <summary>
    /// The goal/competency being rated when it maps to one of the employee's cycle goals (FK to
    /// <see cref="Goal"/>; null when this item rates a named tenant competency instead).
    /// </summary>
    public Guid? GoalId { get; set; }

    /// <summary>
    /// A stable key identifying the competency being rated when it is NOT one of the employee's goals
    /// (e.g. "Leadership", "Communication"). Null when <see cref="GoalId"/> is set. Max 200 chars.
    /// </summary>
    public string? CompetencyKey { get; set; }

    /// <summary>The rating on the cycle's configured scale (FR-4). Integer in [1, cycle.RatingScaleMax].</summary>
    public int Rating { get; set; }

    /// <summary>Optional free-text comment for this competency/goal (FR-4, max 2000 chars).</summary>
    public string? Comment { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public Feedback360? Feedback360 { get; set; }
    public Goal? Goal { get; set; }
}
