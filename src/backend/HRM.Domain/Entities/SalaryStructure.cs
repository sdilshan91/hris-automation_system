namespace HRM.Domain.Entities;

/// <summary>
/// A named salary structure — a tenant's compensation template composed of linked salary components
/// (US-PAY-001 FR-2). Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter
/// + <c>TenantInterceptor</c>. Maps to the "salary_structure" table.
/// </summary>
public sealed class SalaryStructure : BaseEntity
{
    /// <summary>Display name (FR-2, required, max 100 chars), e.g. "Full-Time India".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Short code, unique per tenant (FR-2/BR-2). Stored upper-cased.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Optional free-text description (FR-2).</summary>
    public string? Description { get; set; }

    /// <summary>Date from which this structure version is effective (FR-2/FR-7, required).</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>
    /// Default-structure flag (BR-1). At most ONE structure per tenant may be the default; the service
    /// clears any prior default when a new one is set.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Active flag (FR-2/FR-5, default true). A structure can only be marked active once it has at least
    /// one earning component (FR-5), enforced in the service/validator.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Component links for this structure (FR-3). Owned collection; loaded explicitly where needed.</summary>
    public List<SalaryStructureComponent> Components { get; set; } = [];
}
