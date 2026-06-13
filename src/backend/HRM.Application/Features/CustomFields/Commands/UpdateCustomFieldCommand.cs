using HRM.Application.Common.Models;
using HRM.Application.Features.CustomFields.DTOs;
using MediatR;

namespace HRM.Application.Features.CustomFields.Commands;

/// <summary>
/// Updates an existing custom field definition (US-CHR-012).
/// Field type and field key are immutable after creation.
/// </summary>
public sealed record UpdateCustomFieldCommand(
    Guid CustomFieldId,
    string FieldName,
    bool IsRequired,
    string? Options,
    int? DisplayOrder
) : IRequest<Result<CustomFieldDefinitionDto>>;
