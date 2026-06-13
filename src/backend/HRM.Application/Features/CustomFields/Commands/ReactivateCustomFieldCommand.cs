using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.CustomFields.Commands;

/// <summary>
/// Reactivates a previously deactivated custom field definition (US-CHR-012 AC-5).
/// Restores visibility with previously stored values intact.
/// </summary>
public sealed record ReactivateCustomFieldCommand(Guid CustomFieldId) : IRequest<Result>;
