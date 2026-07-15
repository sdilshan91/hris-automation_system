using HRM.Application.Common.Models;
using HRM.Application.Features.Locations.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for location CRUD operations (US-CHR-007).
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface ILocationService
{
    /// <summary>
    /// Creates a location. <paramref name="defaultShiftId"/> (US-ATT-011 AC-1) must reference an active,
    /// non-deleted shift in the current tenant, or the call fails with 400 <c>invalid_default_shift</c>;
    /// null is always valid (no Location tier).
    /// </summary>
    Task<Result<LocationDto>> CreateAsync(
        string name, string? addressLine1, string? addressLine2,
        string? city, string? stateProvince, string? country,
        string? postalCode, string timeZone, string? phone, string? countryCode = null,
        Guid? defaultShiftId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a location. <paramref name="defaultShiftId"/> (US-ATT-011 AC-1) must reference an active,
    /// non-deleted shift in the current tenant, or the call fails with 400 <c>invalid_default_shift</c>;
    /// null clears the Location tier.
    /// </summary>
    Task<Result<LocationDto>> UpdateAsync(
        Guid locationId, string name, string? addressLine1, string? addressLine2,
        string? city, string? stateProvince, string? country,
        string? postalCode, string timeZone, string? phone, string? countryCode = null,
        Guid? defaultShiftId = null,
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
