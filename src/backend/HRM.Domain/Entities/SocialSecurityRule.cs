using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// The contribution parameters for a social-security <see cref="StatutoryRule"/> — EPF, ETF, professional
/// tax, or a custom item (US-PAY-006 FR-2). Exactly one row per owning rule. The contribution is computed on
/// the wage base named by <see cref="ApplicableOn"/>, capped at <see cref="WageCeilingAnnual"/> (AC-2/BR-8):
/// <c>employee = min(base, ceiling) * employee_rate</c>; <c>employer = min(base, ceiling) * employer_rate</c>.
/// Tenant-scoped via the EF global query filter + <c>TenantInterceptor</c>. Maps to the
/// "social_security_rule" table.
/// </summary>
public sealed class SocialSecurityRule : BaseEntity
{
    /// <summary>FK to the owning statutory rule (required, unique — one social-security rule per rule).</summary>
    public Guid StatutoryRuleId { get; set; }

    /// <summary>Employee contribution rate, percent 0..100 (numeric(5,2)).</summary>
    public decimal EmployeeRate { get; set; }

    /// <summary>Employer contribution rate, percent 0..100 (numeric(5,2)).</summary>
    public decimal EmployerRate { get; set; }

    /// <summary>
    /// Annual wage ceiling for the contribution base; null = no ceiling (AC-2/BR-8). The per-period
    /// (monthly) ceiling is <c>annual / 12</c> by default (numeric(18,2)).
    /// </summary>
    public decimal? WageCeilingAnnual { get; set; }

    /// <summary>The wage base the contribution is computed on (FR-2, required).</summary>
    public StatutoryApplicableOn ApplicableOn { get; set; }

    /// <summary>
    /// Component ids whose amounts form the base when <see cref="ApplicableOn"/> is
    /// <see cref="StatutoryApplicableOn.Custom"/> (FR-2). Stored as a Postgres uuid[] column; null/empty
    /// otherwise.
    /// </summary>
    public List<Guid>? ApplicableComponentIds { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public StatutoryRule? Rule { get; set; }
}
