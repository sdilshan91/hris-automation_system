using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// One configurable income-tax exemption belonging to an income-tax <see cref="StatutoryRule"/> (TAX-2). An
/// exemption REDUCES the income-tax taxable base (never EPF/ETF). As a CHILD of the rule it inherits the
/// rule's country + fiscal year + effective dates (mirroring how <see cref="TaxSlab"/> is a child), so
/// exemptions are automatically country- and period-scoped. Tenant-scoped via the EF global query filter +
/// <c>TenantInterceptor</c> + Postgres RLS. Maps to the "statutory_exemption" table.
/// </summary>
public sealed class StatutoryExemption : BaseEntity
{
    /// <summary>FK to the owning income-tax statutory rule (required).</summary>
    public Guid StatutoryRuleId { get; set; }

    /// <summary>Display name, e.g. "Standard Deduction" (required, max 100).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>How the exemption amount is computed (FlatAmount / PercentOfGross / PercentOfComponent).</summary>
    public ExemptionCalculationType CalculationType { get; set; }

    /// <summary>A flat currency amount (FlatAmount) OR a percentage 0..100 (PercentOfGross/PercentOfComponent).</summary>
    public decimal Value { get; set; }

    /// <summary>The salary component the percentage is taken of — required only for PercentOfComponent.</summary>
    public Guid? ComponentId { get; set; }

    /// <summary>Optional cap on the computed monthly exemption amount.</summary>
    public decimal? MaxAmount { get; set; }

    /// <summary>
    /// True = <see cref="Value"/>/<see cref="MaxAmount"/> are ANNUAL figures → the monthly run applies 1/12;
    /// false = per-period (monthly) as-is. A percentage is inherently per-period (annual does not scale it).
    /// </summary>
    public bool IsAnnual { get; set; }

    /// <summary>Ordering of the exemption within the rule (required, 0-based).</summary>
    public int OrderIndex { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public StatutoryRule? Rule { get; set; }
}
