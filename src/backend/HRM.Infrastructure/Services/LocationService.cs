using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Locations.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Location CRUD service (US-CHR-007).
/// All queries are tenant-scoped via ITenantContext and EF global query filters.
/// </summary>
public sealed class LocationService : ILocationService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<LocationService> _logger;

    public LocationService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<LocationService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new location (AC-2). Validates name uniqueness (FR-2, BR-1).
    /// </summary>
    public async Task<Result<LocationDto>> CreateAsync(
        string name, string? addressLine1, string? addressLine2,
        string? city, string? stateProvince, string? country,
        string? postalCode, string timeZone, string? phone,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LocationDto>.Failure("Tenant context is not resolved.", 400);

        // FR-2 / BR-1: name uniqueness within tenant
        var nameExists = await _dbContext.Locations
            .AnyAsync(l => l.Name == name, cancellationToken);

        if (nameExists)
            return Result<LocationDto>.Failure("A location with this name already exists.", 400);

        var location = new Location
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            Name = name,
            AddressLine1 = addressLine1,
            AddressLine2 = addressLine2,
            City = city,
            StateProvince = stateProvince,
            Country = country,
            PostalCode = postalCode,
            TimeZone = timeZone,
            Phone = phone,
            IsActive = true,
            IsDeleted = false,
        };

        _dbContext.Locations.Add(location);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Location created. Id={LocationId}, Name={LocationName}, TenantId={TenantId}, By={User}",
            location.Id, location.Name, _tenantContext.TenantId, _currentUser.Email);

        return Result<LocationDto>.Success(ToDto(location));
    }

    /// <summary>
    /// Updates a location (AC-4). Validates name uniqueness excluding self.
    /// </summary>
    public async Task<Result<LocationDto>> UpdateAsync(
        Guid locationId, string name, string? addressLine1, string? addressLine2,
        string? city, string? stateProvince, string? country,
        string? postalCode, string timeZone, string? phone,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LocationDto>.Failure("Tenant context is not resolved.", 400);

        var location = await _dbContext.Locations
            .FirstOrDefaultAsync(l => l.Id == locationId, cancellationToken);

        if (location is null)
            return Result<LocationDto>.Failure("Location not found.", 404);

        // Name uniqueness (excluding self)
        var nameExists = await _dbContext.Locations
            .AnyAsync(l => l.Name == name && l.Id != locationId, cancellationToken);

        if (nameExists)
            return Result<LocationDto>.Failure("A location with this name already exists.", 400);

        location.Name = name;
        location.AddressLine1 = addressLine1;
        location.AddressLine2 = addressLine2;
        location.City = city;
        location.StateProvince = stateProvince;
        location.Country = country;
        location.PostalCode = postalCode;
        location.TimeZone = timeZone;
        location.Phone = phone;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Location updated. Id={LocationId}, Name={LocationName}, TenantId={TenantId}, By={User}",
            location.Id, location.Name, _tenantContext.TenantId, _currentUser.Email);

        return Result<LocationDto>.Success(ToDto(location));
    }

    /// <summary>
    /// Deactivates a location (AC-3, FR-5).
    /// Blocks deactivation when active employees are assigned via LocationId FK.
    /// </summary>
    public async Task<Result> DeactivateAsync(
        Guid locationId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var location = await _dbContext.Locations
            .FirstOrDefaultAsync(l => l.Id == locationId, cancellationToken);

        if (location is null)
            return Result.Failure("Location not found.", 404);

        if (!location.IsActive)
            return Result.Failure("Location is already deactivated.", 400);

        // AC-3 / FR-5: Cannot deactivate if there are active employees assigned.
        var activeEmployeeCount = await _dbContext.Employees
            .CountAsync(e => e.LocationId == locationId && e.IsActive, cancellationToken);

        if (activeEmployeeCount > 0)
            return Result.Failure(
                $"This location has {activeEmployeeCount} active employee(s). Reassign them before deactivating.",
                400);

        location.IsActive = false;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Location deactivated. Id={LocationId}, Name={LocationName}, TenantId={TenantId}, By={User}",
            location.Id, location.Name, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    public async Task<Result<LocationDto>> GetByIdAsync(
        Guid locationId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LocationDto>.Failure("Tenant context is not resolved.", 400);

        var location = await _dbContext.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == locationId, cancellationToken);

        if (location is null)
            return Result<LocationDto>.Failure("Location not found.", 404);

        var employeeCount = await _dbContext.Employees
            .CountAsync(e => e.LocationId == locationId && e.IsActive, cancellationToken);

        return Result<LocationDto>.Success(ToDto(location, employeeCount));
    }

    public async Task<Result<IReadOnlyList<LocationDto>>> GetAllAsync(
        bool? activeOnly = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<LocationDto>>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.Locations.AsNoTracking();

        if (activeOnly == true)
            query = query.Where(l => l.IsActive);

        var locations = await query
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);

        // Batch-load employee counts for all locations in one query
        var locationIds = locations.Select(l => l.Id).ToList();
        var employeeCounts = await _dbContext.Employees
            .Where(e => e.LocationId != null && locationIds.Contains(e.LocationId.Value) && e.IsActive)
            .GroupBy(e => e.LocationId!.Value)
            .Select(g => new { LocationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.LocationId, g => g.Count, cancellationToken);

        var dtos = locations.Select(l =>
            ToDto(l, employeeCounts.GetValueOrDefault(l.Id, 0))).ToList();
        return Result<IReadOnlyList<LocationDto>>.Success(dtos);
    }

    // -- Private helpers --

    private static LocationDto ToDto(Location l, int? employeeCount = null) => new()
    {
        Id = l.Id,
        Name = l.Name,
        AddressLine1 = l.AddressLine1,
        AddressLine2 = l.AddressLine2,
        City = l.City,
        StateProvince = l.StateProvince,
        Country = l.Country,
        PostalCode = l.PostalCode,
        TimeZone = l.TimeZone,
        Phone = l.Phone,
        EmployeeCount = employeeCount ?? 0,
        IsActive = l.IsActive,
        CreatedAt = l.CreatedAt,
        UpdatedAt = l.UpdatedAt,
    };
}
