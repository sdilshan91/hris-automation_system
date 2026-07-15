namespace HRM.Domain.Entities;

/// <summary>
/// Represents an office location or branch within a tenant.
/// Supports tenant-scoped CRUD, soft-delete, and IANA time zone tracking.
/// US-CHR-007: Manage Office Locations.
/// </summary>
public sealed class Location : BaseEntity, IAuditExempt
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
    /// Country name (from a standard list). Free-text, used for display only.
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// ISO country code (alpha-2/alpha-3, max 5), e.g. "LK", "IN". Multi-country tax foundation: this is the
    /// machine-readable code that JOINS to <c>StatutoryRule.CountryCode</c> so an employee is taxed under their
    /// branch/location's country. Optional; the free-text <see cref="Country"/> stays for display. When null the
    /// employee falls back to the tenant default country (Tenant.DefaultCountryCode) at payroll time.
    /// </summary>
    public string? CountryCode { get; set; }

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

    /// <summary>
    /// US-CHR-009 BR-6 (ISSUE-304): this Location's probation-period override, in days from date of joining.
    /// <b>Null = inherit <see cref="Tenant.ProbationPeriodDays"/></b> — the effective period is
    /// <c>Location.ProbationPeriodDays ?? Tenant.ProbationPeriodDays</c>. Nullable precisely so a Location can
    /// stay SILENT: a non-nullable column with a default would make every Location an override of its tenant
    /// and the tenant setting would never apply anywhere.
    /// </summary>
    public int? ProbationPeriodDays { get; set; }

    /// <summary>
    /// Optional per-location default <see cref="Shift"/> — the <b>Location tier</b> of the working-calendar
    /// resolution chain <b>Employee → Location → Tenant → code default</b> (US-ATT-011 AC-1/AC-2, BR-3).
    /// It is what lets a Gulf branch run Sun–Thu while the tenant default stays Mon–Fri, with no per-employee
    /// shift assignment. Null (the default, and the single-branch case) = fall through to the tenant default
    /// shift, then to the Mon–Fri code default. Must reference an active, non-deleted shift in the SAME tenant;
    /// a cross-tenant id is invisible under the EF global query filter and is rejected at write time (BR-1).
    /// </summary>
    public Guid? DefaultShiftId { get; set; }

    // -- Navigation --

    /// <summary>
    /// The shift referenced by <see cref="DefaultShiftId"/>, if any.
    /// </summary>
    public Shift? DefaultShift { get; set; }

    /// <summary>
    /// Employees assigned to this location.
    /// </summary>
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
