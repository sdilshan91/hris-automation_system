namespace HRM.Application.Features.CustomFields.DTOs;

/// <summary>
/// DTO for a custom field definition (US-CHR-012 AC-1).
/// </summary>
public sealed record CustomFieldDefinitionDto
{
    public Guid Id { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public string FieldName { get; init; } = string.Empty;
    public string FieldKey { get; init; } = string.Empty;
    public string FieldType { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public string? Options { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsActive { get; init; }
    public int UsageCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Grouped result for listing custom fields by entity type (AC-1).
/// </summary>
public sealed record CustomFieldDefinitionListResult
{
    public string EntityType { get; init; } = string.Empty;
    public IReadOnlyList<CustomFieldDefinitionDto> Fields { get; init; } = [];
    public int TotalCount { get; init; }
    public int MaxAllowed { get; init; }
}

/// <summary>
/// Request for creating a custom field definition (US-CHR-012 AC-2).
/// </summary>
public sealed record CreateCustomFieldRequest
{
    public string EntityType { get; init; } = "employee";
    public string FieldName { get; init; } = string.Empty;
    public string? FieldKey { get; init; }
    public string FieldType { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public string? Options { get; init; }
    public int? DisplayOrder { get; init; }
}

/// <summary>
/// Request for updating a custom field definition.
/// </summary>
public sealed record UpdateCustomFieldRequest
{
    public string FieldName { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public string? Options { get; init; }
    public int? DisplayOrder { get; init; }
}

/// <summary>
/// Request for reordering custom fields (FR-8).
/// </summary>
public sealed record ReorderCustomFieldsRequest
{
    /// <summary>
    /// Ordered list of custom field IDs in the desired display order.
    /// </summary>
    public IReadOnlyList<Guid> FieldIds { get; init; } = [];
}
