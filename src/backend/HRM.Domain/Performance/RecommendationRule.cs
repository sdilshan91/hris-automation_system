using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Performance;

/// <summary>
/// A tenant-configurable rating-threshold rule that drives auto-generation of recommendation SUGGESTIONS
/// (US-PRF-010 FR-2/AC-2/BR-3). Each rule maps a minimum final score to a recommendation type — e.g.
/// "final score ≥ 4.5 ⇒ Promotion eligible", "≥ 3.5 ⇒ Bonus eligible". Rules are CONFIG, not hard-coded
/// (§10 assumption): they are stored per tenant and edited via the API, and the auto-generation handler applies
/// them across a cycle's employees. The highest-threshold matching rule wins per employee (most generous reward).
/// Auto-generated rows are always Draft suggestions (BR-3 — HR reviews + submits). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c> (NFR-2). Maps to the
/// "recommendation_rule" table.
/// </summary>
public sealed class RecommendationRule : BaseEntity
{
    /// <summary>Human label for the rule, e.g. "Top performers → Promotion".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The minimum final score (inclusive) an employee must reach for this rule to apply (FR-2).</summary>
    public decimal MinFinalScore { get; set; }

    /// <summary>The recommendation type this rule suggests when the threshold is met (FR-2).</summary>
    public RecommendationType RecommendedType { get; set; }

    /// <summary>
    /// The default bonus percentage to seed on an auto-generated Bonus suggestion (FR-2, optional). Lets the rule
    /// pre-fill a suggested figure for HR to review; null leaves the amount blank.
    /// </summary>
    public decimal? DefaultBonusPercent { get; set; }

    /// <summary>The default increment percentage to seed on an auto-generated Increment suggestion (FR-2, optional).</summary>
    public decimal? DefaultIncrementPercent { get; set; }

    /// <summary>Whether the rule is active. Inactive rules are skipped by auto-generation.</summary>
    public bool IsActive { get; set; } = true;
}
