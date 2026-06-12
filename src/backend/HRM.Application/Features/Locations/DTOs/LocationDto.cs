namespace HRM.Application.Features.Locations.DTOs;

/// <summary>
/// Flat DTO for a single location (list/detail views).
/// </summary>
public sealed record LocationDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? StateProvince { get; init; }
    public string? Country { get; init; }
    public string? PostalCode { get; init; }
    public string TimeZone { get; init; } = string.Empty;
    public string? Phone { get; init; }
    /// <summary>
    /// Count of active employees assigned to this location (FR-7).
    /// </summary>
    public int EmployeeCount { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Request body for creating a location.
/// </summary>
public sealed record CreateLocationRequest
{
    public string Name { get; init; } = string.Empty;
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? StateProvince { get; init; }
    public string? Country { get; init; }
    public string? PostalCode { get; init; }
    public string TimeZone { get; init; } = string.Empty;
    public string? Phone { get; init; }
}

/// <summary>
/// Request body for updating a location.
/// </summary>
public sealed record UpdateLocationRequest
{
    public string Name { get; init; } = string.Empty;
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? StateProvince { get; init; }
    public string? Country { get; init; }
    public string? PostalCode { get; init; }
    public string TimeZone { get; init; } = string.Empty;
    public string? Phone { get; init; }
}
