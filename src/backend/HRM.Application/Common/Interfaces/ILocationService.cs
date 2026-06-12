using HRM.Application.Common.Models;
using HRM.Application.Features.Locations.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for location CRUD operations (US-CHR-007).
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface ILocationService
{
    Task<Result<LocationDto>> CreateAsync(
        string name, string? addressLine1, string? addressLine2,
        string? city, string? stateProvince, string? country,
        string? postalCode, string timeZone, string? phone,
        CancellationToken cancellationToken = default);

    Task<Result<LocationDto>> UpdateAsync(
        Guid locationId, string name, string? addressLine1, string? addressLine2,
        string? city, string? stateProvince, string? country,
        string? postalCode, string timeZone, string? phone,
        CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(
        Guid locationId,
        CancellationToken cancellationToken = default);

    Task<Result<LocationDto>> GetByIdAsync(
        Guid locationId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<LocationDto>>> GetAllAsync(
        bool? activeOnly = null,
        CancellationToken cancellationToken = default);
}
