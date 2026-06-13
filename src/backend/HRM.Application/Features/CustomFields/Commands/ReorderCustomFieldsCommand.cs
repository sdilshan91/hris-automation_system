using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.CustomFields.Commands;

/// <summary>
/// Reorders custom fields by display_order (US-CHR-012 FR-8).
/// </summary>
public sealed record ReorderCustomFieldsCommand(
    string EntityType,
    IReadOnlyList<Guid> FieldIds
) : IRequest<Result>;
