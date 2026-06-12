using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Locations.Commands;

/// <summary>
/// Deactivates (soft-deactivates) a location (US-CHR-007 AC-3, FR-5).
/// Blocks if active employees are assigned to the location.
/// </summary>
public sealed record DeactivateLocationCommand(
    Guid LocationId
) : IRequest<Result>;
