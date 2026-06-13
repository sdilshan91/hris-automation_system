namespace HRM.Domain.Entities;

/// <summary>
/// Defines a custom field that a tenant has configured for an entity type (Phase 1: Employee).
/// Tenant-scoped via BaseEntity.TenantId. Supports soft-delete (IsDeleted).
/// US-CHR-012: Custom Fields per Tenant.
/// </summary>
public sealed class CustomFieldDefinition : BaseEntity
{
    /// <summary>
    /// The entity type this custom field applies to. Phase 1: "employee".
    /// Extensible to other entity types in future phases.
    /// </summary>
    public string EntityType { get; set; } = "employee";

    /// <summary>
    /// Human-readable field name, unique per tenant + entity_type (BR-1).
    /// Example: "T-Shirt Size".
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// URL-safe slugified key used as the JSONB property key. Immutable after creation.
    /// Example: "tshirt_size".
    /// </summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>
    /// The data type of this field (FR-2):
    /// text, textarea, number, date, dropdown, multi_select, checkbox, email, phone, url.
    /// </summary>
    public string FieldType { get; set; } = string.Empty;

    /// <summary>
    /// Whether a value is required when saving the entity (FR-5).
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// JSONB array of allowed options for dropdown and multi_select field types.
    /// Example: ["S", "M", "L", "XL"]. Null for non-option types.
    /// </summary>
    public string? Options { get; set; }

    /// <summary>
    /// Controls the rendering order on forms (FR-8).
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether this field is currently active and visible on forms (FR-7).
    /// Deactivated fields are hidden but preserve their JSONB data (BR-3).
    /// </summary>
    public bool IsActive { get; set; } = true;
}
