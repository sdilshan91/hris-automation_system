namespace HRM.Domain.Entities;

/// <summary>
/// Represents an office location or branch within a tenant.
/// Supports tenant-scoped CRUD, soft-delete, and IANA time zone tracking.
/// US-CHR-007: Manage Office Locations.
/// </summary>
public sealed class Location : BaseEntity
{
    /// <summary>
    /// Location name, unique within a tenant (FR-2, BR-1).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Street address line 1.
    /// </summary>
    public string? AddressLine1 { get; set; }

    /// <summary>
    /// Street address line 2.
    /// </summary>
    public string? AddressLine2 { get; set; }

    /// <summary>
    /// City name.
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// State or province.
    /// </summary>
    public string? StateProvince { get; set; }

    /// <summary>
    /// Country name (from a standard list).
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Postal/ZIP code.
    /// </summary>
    public string? PostalCode { get; set; }

    /// <summary>
    /// IANA time zone identifier, e.g. "Asia/Colombo" (FR-4, required).
    /// </summary>
    public string TimeZone { get; set; } = string.Empty;

    /// <summary>
    /// Location phone number, optional.
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Whether the location is active (FR-5, BR-5).
    /// Deactivated locations are hidden from assignment dropdowns but visible in admin views.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // -- Navigation --

    /// <summary>
    /// Employees assigned to this location.
    /// </summary>
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
