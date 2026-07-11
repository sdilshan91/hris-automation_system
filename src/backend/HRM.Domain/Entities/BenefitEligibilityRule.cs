using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A tenant-scoped eligibility rule for a <see cref="BenefitPlan"/> (US-TRN-003 FR-1). All of a plan's rules are
/// ANDed against an employee's Core HR attributes by <c>BenefitEligibilityEvaluator</c> (BR-1). No rules on a plan
/// means it is open to all active employees (AC-1). The <see cref="Operator"/> vocabulary mirrors the workflow
/// condition operators (<c>== != &gt; &gt;= &lt; &lt;=</c>) plus <c>In</c> for set membership.
/// </summary>
public sealed class BenefitEligibilityRule : BaseEntity
{
    /// <summary>The plan this rule qualifies (FK → <see cref="BenefitPlan"/>).</summary>
    public Guid BenefitPlanId { get; set; }

    /// <summary>The employee attribute tested (FR-1). <c>JobGrade</c> maps to the employee's job-title id.</summary>
    public EligibilityAttribute Attribute { get; set; }

    /// <summary>Comparison operator: one of <c>== != &gt; &gt;= &lt; &lt;= In</c>. Legality depends on the attribute.</summary>
    public string Operator { get; set; } = "==";

    /// <summary>The comparison value — a scalar (enum name, int, or Guid) or a comma-separated list for <c>In</c>.</summary>
    public string Value { get; set; } = string.Empty;
}
