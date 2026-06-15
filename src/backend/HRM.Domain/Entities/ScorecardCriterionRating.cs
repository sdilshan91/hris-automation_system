namespace HRM.Domain.Entities;

/// <summary>
/// A single criterion rating on an <see cref="InterviewScorecard"/> (US-REC-006 FR-1). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> and the EF global query filter + <c>TenantInterceptor</c>. Maps to the
/// "scorecard_criterion_rating" table. Modeled as its own tenant-scoped entity (not an EF owned type) so it
/// carries the tenant id / audit columns consistently with the rest of the module.
/// </summary>
public sealed class ScorecardCriterionRating : BaseEntity
{
    /// <summary>FK to the parent <see cref="InterviewScorecard"/> (required). Plain UUID column.</summary>
    public Guid ScorecardId { get; set; }

    /// <summary>
    /// The criterion this rating is for (FR-1) — a stable key from the default criteria set
    /// (<c>ScorecardCriteria.Default</c>, BR-2). Validated against the known keys in the service.
    /// </summary>
    public string CriterionKey { get; set; } = string.Empty;

    /// <summary>The 1-5 rating for this criterion (FR-1, required).</summary>
    public int Score { get; set; }

    /// <summary>Optional per-criterion comment (FR-1).</summary>
    public string? Comment { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    public InterviewScorecard? Scorecard { get; set; }
}
