namespace HRM.Domain.Entities;

/// <summary>
/// Junction linking a <see cref="SalaryComponent"/> to a <see cref="SalaryStructure"/> with
/// per-structure overrides (US-PAY-001 FR-3). Tenant-scoped via <see cref="BaseEntity.TenantId"/> +
/// the EF global query filter + <c>TenantInterceptor</c>. Maps to the "salary_structure_component" table.
/// </summary>
public sealed class SalaryStructureComponent : BaseEntity
{
    /// <summary>FK to the owning salary structure (FR-3, required).</summary>
    public Guid SalaryStructureId { get; set; }

    /// <summary>FK to the linked salary component (FR-3, required).</summary>
    public Guid SalaryComponentId { get; set; }

    /// <summary>
    /// Per-structure override of the component's amount/percentage (FR-3). Null = use the component's
    /// default. numeric(18,2) (§7).
    /// </summary>
    public decimal? OverrideValue { get; set; }

    /// <summary>Per-structure override of a formula component's expression (FR-3). Null = use the component default.</summary>
    public string? OverrideFormula { get; set; }

    /// <summary>Processing priority within this structure (FR-3/AC-4, required). Lower = earlier.</summary>
    public int ProcessingOrder { get; set; }

    /// <summary>Whether this component is mandatory within the structure (FR-3, default false).</summary>
    public bool IsMandatory { get; set; }

    // ── Navigation ─────────────────────────────────────────────────
    public SalaryStructure? Structure { get; set; }
    public SalaryComponent? Component { get; set; }
}
