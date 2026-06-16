using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// One employee self-rating row within a <see cref="SelfAssessment"/> — the employee's perspective on a
/// single goal (US-PRF-002 FR-1/FR-3). Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global
/// query filter + <c>TenantInterceptor</c>. Maps to the "self_assessment_item" table.
/// </summary>
public sealed class SelfAssessmentItem : BaseEntity
{
    /// <summary>The parent self-assessment (FK, required).</summary>
    public Guid SelfAssessmentId { get; set; }

    /// <summary>The goal being self-rated (FK to <see cref="Goal"/>, required).</summary>
    public Guid GoalId { get; set; }

    /// <summary>
    /// Self-rating on the cycle's configured scale (FR-2). Integer in [1, cycle.RatingScaleMax]. Null while a
    /// draft is partially filled; required (non-null) before submit (BR-2).
    /// </summary>
    public int? SelfRating { get; set; }

    /// <summary>Achievement percentage for this goal (FR-1). 0-100. Null while draft; required before submit.</summary>
    public int? AchievementPercentage { get; set; }

    /// <summary>
    /// Self-assessment comment for this goal (FR-3). Required at submit with a minimum of 20 characters;
    /// may be null/short in a draft. Stored as plain text (Assumptions §10).
    /// </summary>
    public string? Comment { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public SelfAssessment? SelfAssessment { get; set; }
    public Goal? Goal { get; set; }
    public List<SelfAssessmentAttachment> Attachments { get; set; } = [];
}
