using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.CustomFields.Commands;

/// <summary>
/// Deactivates a custom field definition (US-CHR-012 FR-7, AC-5).
/// The field is hidden from forms but JSONB data is preserved.
/// </summary>
public sealed record DeactivateCustomFieldCommand(Guid CustomFieldId) : IRequest<Result>;
