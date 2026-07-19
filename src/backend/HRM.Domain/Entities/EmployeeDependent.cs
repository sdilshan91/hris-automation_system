namespace HRM.Domain.Entities;

/// <summary>
/// Dependent (family member) of an employee (US-CHR-002 / DF-39).
/// Tenant-scoped via BaseEntity.TenantId. Full-replace managed in UpdateProfileAsync.
/// DateOfBirth uses <see cref="DateOnly"/> → PostgreSQL <c>date</c> (no time-of-day, no timezone/Kind concerns).
/// </summary>
public sealed class EmployeeDependent : BaseEntity
{
    /// <summary>
    /// FK to the employee this dependent belongs to.
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// Dependent's full name (required).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Relationship to the employee (e.g. Spouse, Child, Parent) (required).
    /// </summary>
    public string Relationship { get; set; } = string.Empty;

    /// <summary>
    /// Dependent's date of birth (optional; date only).
    /// </summary>
    public DateOnly? DateOfBirth { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    public Employee? Employee { get; set; }
}
