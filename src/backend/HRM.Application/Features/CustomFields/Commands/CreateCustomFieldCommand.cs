using HRM.Application.Common.Models;
using HRM.Application.Features.CustomFields.DTOs;
using MediatR;

namespace HRM.Application.Features.CustomFields.Commands;

/// <summary>
/// Creates a new custom field definition in the current tenant (US-CHR-012 AC-2).
/// </summary>
public sealed record CreateCustomFieldCommand(
    string EntityType,
    string FieldName,
    string? FieldKey,
    string FieldType,
    bool IsRequired,
    string? Options,
    int? DisplayOrder
) : IRequest<Result<CustomFieldDefinitionDto>>;
