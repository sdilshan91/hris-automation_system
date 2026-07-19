namespace HRM.Domain.Entities;

/// <summary>
/// Prior work-history entry for an employee (US-CHR-002 / DF-39).
/// Tenant-scoped via BaseEntity.TenantId. Full-replace managed in UpdateProfileAsync.
/// Dates use <see cref="DateOnly"/> → PostgreSQL <c>date</c> (no time-of-day, no timezone/Kind concerns).
/// </summary>
public sealed class EmployeeWorkHistory : BaseEntity
{
    /// <summary>
    /// FK to the employee this work-history entry belongs to.
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// Former employer / company name (required).
    /// </summary>
    public string Company { get; set; } = string.Empty;

    /// <summary>
    /// Position / title held (required).
    /// </summary>
    public string Position { get; set; } = string.Empty;

    /// <summary>
    /// Employment start date (optional; date only).
    /// </summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>
    /// Employment end date (optional; date only).
    /// </summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>
    /// Free-text description of responsibilities (optional).
    /// </summary>
    public string? Description { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    public Employee? Employee { get; set; }
}
