using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A single company asset in the lite asset register (US-ONB-004 FR-2). Created either ad-hoc during an
/// issuance or pre-seeded into the register, then issued to a new hire during onboarding. An asset can be
/// assigned to only one employee at a time (BR-1); its <see cref="AssetTag"/> is unique per tenant and its
/// <see cref="SerialNumber"/> is unique per tenant when provided (BR-3). Soft-deletable (BR-5) — never
/// hard-deleted via the UI. Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the global query filter
/// (NFR-2). Maps to the "asset" table (snake_case).
/// </summary>
public sealed class Asset : BaseEntity
{
    /// <summary>
    /// Asset type, e.g. "Laptop", "Phone", "ID Card", "Access Badge", "Vehicle" (FR-2, BR-2). Validated
    /// against the tenant's configured type set (currently a default seeded set — see AssetService).
    /// </summary>
    public string AssetType { get; set; } = string.Empty;

    /// <summary>Human-readable asset tag/label (FR-2). Unique per tenant (BR-3).</summary>
    public string AssetTag { get; set; } = string.Empty;

    /// <summary>Manufacturer serial number (FR-2). Optional; unique per tenant when provided (BR-3).</summary>
    public string? SerialNumber { get; set; }

    /// <summary>Brand/manufacturer (FR-2). Optional.</summary>
    public string? Brand { get; set; }

    /// <summary>Model name/number (FR-2). Optional.</summary>
    public string? Model { get; set; }

    /// <summary>Purchase date (FR-2). Optional.</summary>
    public DateTime? PurchaseDate { get; set; }

    /// <summary>Condition at issuance (FR-2, §7).</summary>
    public AssetCondition Condition { get; set; } = AssetCondition.Good;

    /// <summary>Lifecycle status (FR-2). New register entries start <see cref="AssetStatus.Available"/>.</summary>
    public AssetStatus Status { get; set; } = AssetStatus.Available;

    /// <summary>The employee the asset is currently assigned to (FR-4, BR-1). Null when not assigned.</summary>
    public Guid? AssignedEmployeeId { get; set; }

    /// <summary>Date the asset was issued (§7). Set on issuance; cannot be in the future.</summary>
    public DateTime? IssueDate { get; set; }

    /// <summary>
    /// FR-6: tenant-isolated storage key of the signed acknowledgment receipt uploaded with the issuance
    /// (null if none). Stored via IFileStorage.
    /// </summary>
    public string? AcknowledgmentDocKey { get; set; }

    /// <summary>FR-6: original file name of the acknowledgment document (for download display).</summary>
    public string? AcknowledgmentDocFileName { get; set; }

    /// <summary>Free-text notes (§7, max 500).</summary>
    public string? Notes { get; set; }

    /// <summary>Navigation to the currently-assigned employee (no cascade; soft-ref by id otherwise).</summary>
    public Employee? AssignedEmployee { get; set; }
}
