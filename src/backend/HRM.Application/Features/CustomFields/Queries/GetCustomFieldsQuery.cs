using HRM.Application.Common.Models;
using HRM.Application.Features.CustomFields.DTOs;
using MediatR;

namespace HRM.Application.Features.CustomFields.Queries;

/// <summary>
/// Lists all custom field definitions for the current tenant, grouped by entity type (US-CHR-012 AC-1).
/// </summary>
public sealed record GetCustomFieldsQuery(string? EntityType = null)
    : IRequest<Result<IReadOnlyList<CustomFieldDefinitionListResult>>>;
