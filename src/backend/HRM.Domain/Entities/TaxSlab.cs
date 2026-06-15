namespace HRM.Domain.Entities;

/// <summary>
/// One progressive income-tax slab belonging to an income-tax <see cref="StatutoryRule"/> (US-PAY-006 FR-1).
/// Slabs are evaluated progressively — each slab's rate applies only to the income that falls within its
/// [<see cref="SlabFrom"/>, <see cref="SlabTo"/>) range (BR-3). The set of slabs for a rule must be
/// contiguous (no gaps or overlaps — FR-6), enforced by the validator. Tenant-scoped via the EF global
/// query filter + <c>TenantInterceptor</c>. Maps to the "tax_slab" table.
/// </summary>
public sealed class TaxSlab : BaseEntity
{
    /// <summary>FK to the owning income-tax statutory rule (required).</summary>
    public Guid StatutoryRuleId { get; set; }

    /// <summary>Lower bound of the slab, inclusive (numeric(18,2)).</summary>
    public decimal SlabFrom { get; set; }

    /// <summary>Upper bound of the slab, exclusive; null = unlimited (the top slab) (numeric(18,2)).</summary>
    public decimal? SlabTo { get; set; }

    /// <summary>Tax rate applied to income within this slab, percent 0..100 (numeric(5,2)).</summary>
    public decimal RatePercentage { get; set; }

    /// <summary>Ordering of the slab from lowest to highest band (required, 0-based).</summary>
    public int OrderIndex { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public StatutoryRule? Rule { get; set; }
}
