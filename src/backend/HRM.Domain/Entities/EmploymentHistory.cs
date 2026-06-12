namespace HRM.Domain.Entities;

/// <summary>
/// Append-only employment history timeline entry (US-CHR-002 FR-6, AC-6, BR-4).
/// Records changes to department, job title, status, and reporting manager with effective dates.
/// </summary>
public sealed class EmploymentHistory : BaseEntity
{
    /// <summary>
    /// FK to the employee this history entry belongs to.
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// The type of change: Department, JobTitle, Status, ReportingManager.
    /// </summary>
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>
    /// Previous value (e.g., department name, job title, status).
    /// Null for the initial entry.
    /// </summary>
    public string? PreviousValue { get; set; }

    /// <summary>
    /// New value after the change.
    /// </summary>
    public string NewValue { get; set; } = string.Empty;

    /// <summary>
    /// Previous referenced entity ID (e.g., old DepartmentId).
    /// </summary>
    public Guid? PreviousReferenceId { get; set; }

    /// <summary>
    /// New referenced entity ID (e.g., new DepartmentId).
    /// </summary>
    public Guid? NewReferenceId { get; set; }

    /// <summary>
    /// The date the change takes effect (may differ from CreatedAt).
    /// </summary>
    public DateTime EffectiveDate { get; set; }

    /// <summary>
    /// Free-text reason or notes for the change.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Who initiated the change (user email or system).
    /// </summary>
    public string ChangedBy { get; set; } = string.Empty;

    // ── Navigation ─────────────────────────────────────────────────

    public Employee? Employee { get; set; }
}
