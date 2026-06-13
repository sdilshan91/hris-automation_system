using HRM.Application.Common.Models;
using HRM.Application.Features.CustomFields.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for custom field definition CRUD and value validation (US-CHR-012).
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface ICustomFieldService
{
    /// <summary>
    /// Lists all custom field definitions for the current tenant, grouped by entity type,
    /// with usage count per field (AC-1).
    /// </summary>
    Task<Result<IReadOnlyList<CustomFieldDefinitionListResult>>> GetAllAsync(
        string? entityType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single custom field definition by ID.
    /// </summary>
    Task<Result<CustomFieldDefinitionDto>> GetByIdAsync(
        Guid customFieldId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new custom field definition (AC-2). Validates uniqueness and plan limit.
    /// </summary>
    Task<Result<CustomFieldDefinitionDto>> CreateAsync(
        CreateCustomFieldRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing custom field definition. Field type and field key are immutable.
    /// </summary>
    Task<Result<CustomFieldDefinitionDto>> UpdateAsync(
        Guid customFieldId,
        UpdateCustomFieldRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates a custom field (hides from forms, preserves JSONB data) (FR-7, AC-5).
    /// </summary>
    Task<Result> DeactivateAsync(
        Guid customFieldId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reactivates a previously deactivated custom field (AC-5).
    /// </summary>
    Task<Result> ReactivateAsync(
        Guid customFieldId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reorders custom fields by setting display_order from an ordered list of IDs (FR-8).
    /// </summary>
    Task<Result> ReorderAsync(
        string entityType,
        IReadOnlyList<Guid> fieldIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates custom field values (from an employee's CustomFields JSON) against
    /// active definitions for the given entity type (FR-5, AC-3).
    /// Returns Success if all values are valid, or Failure with a descriptive message.
    /// </summary>
    Task<Result> ValidateCustomFieldValuesAsync(
        string entityType,
        string? customFieldsJson,
        CancellationToken cancellationToken = default);
}
