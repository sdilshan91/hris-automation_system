using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A versioned statutory deduction rule for a tenant (US-PAY-006 FR-1/FR-3/FR-4). One rule owns either a
/// set of <see cref="TaxSlab"/> rows (when <see cref="RuleType"/> is <see cref="StatutoryRuleType.IncomeTax"/>)
/// or a single <see cref="SocialSecurityRule"/> (EPF/ETF/ProfessionalTax/Custom). Rules are
/// fiscal-year-versioned with effective date ranges so historical payroll uses the rule that was in effect
/// at the time (FR-4/BR-5). Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter
/// + <c>TenantInterceptor</c> (Postgres RLS is the deferred US-PLT-002). Maps to the "statutory_rule" table.
///
/// <para>BR-1: phase 1 supports ONE configured country; <see cref="CountryCode"/> is carried so adding more
/// countries later is non-breaking (the calc engine simply filters by country when a multi-country surface
/// lands).</para>
/// </summary>
public sealed class StatutoryRule : BaseEntity, IAuditExempt
{
    /// <summary>Statutory type (FR-3, required). Drives whether tax slabs or a social-security rule apply.</summary>
    public StatutoryRuleType RuleType { get; set; }

    /// <summary>Display name (FR-1, required, max 100), e.g. "PAYE Income Tax" or "EPF".</summary>
    public string RuleName { get; set; } = string.Empty;

    /// <summary>ISO country code (BR-1, required, max 5), e.g. "LK". Single-country in phase 1.</summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>Fiscal-year label (FR-1/FR-4, required, max 10), e.g. "2026-2027".</summary>
    public string FiscalYear { get; set; } = string.Empty;

    /// <summary>Effective-from date, inclusive (FR-1/FR-4, required).</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Effective-to date, inclusive; null = open-ended/current (FR-1/FR-4).</summary>
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>Whether the rule is currently active (FR-1, default true).</summary>
    public bool IsActive { get; set; } = true;

    // ── Navigation ─────────────────────────────────────────────────
    public List<TaxSlab> TaxSlabs { get; set; } = [];

    /// <summary>Configurable income-tax exemptions that reduce the taxable base (TAX-2). Only meaningful for IncomeTax rules.</summary>
    public List<StatutoryExemption> Exemptions { get; set; } = new();
    public SocialSecurityRule? SocialSecurityRule { get; set; }
}
