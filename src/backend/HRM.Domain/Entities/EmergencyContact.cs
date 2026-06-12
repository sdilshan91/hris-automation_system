namespace HRM.Domain.Entities;

/// <summary>
/// Emergency contact for an employee (US-CHR-002 AC-1, AC-4).
/// Tenant-scoped via BaseEntity.TenantId.
/// </summary>
public sealed class EmergencyContact : BaseEntity
{
    /// <summary>
    /// FK to the employee this contact belongs to.
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// Contact person's full name.
    /// </summary>
    public string ContactName { get; set; } = string.Empty;

    /// <summary>
    /// Relationship to the employee (e.g., Spouse, Parent, Sibling).
    /// </summary>
    public string Relationship { get; set; } = string.Empty;

    /// <summary>
    /// Primary phone number.
    /// </summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// Optional secondary phone number.
    /// </summary>
    public string? AlternatePhone { get; set; }

    /// <summary>
    /// Optional email address.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Whether this is the primary emergency contact.
    /// </summary>
    public bool IsPrimary { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    public Employee? Employee { get; set; }
}
