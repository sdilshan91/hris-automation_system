namespace HRM.Domain.Entities;

/// <summary>
/// Field-level audit log for employee profile changes (US-CHR-002 FR-5, AC-2).
/// Stores before/after JSONB snapshots of changed fields.
/// Separate from the generic AuditLog entity which tracks security events.
/// </summary>
public sealed class EmployeeFieldAuditLog
{
    public Guid Id { get; set; }

    /// <summary>
    /// Tenant scoping.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The employee whose profile was changed.
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// The profile section that was changed (e.g., "PersonalInfo", "Contact", "Employment", "EmergencyContacts").
    /// </summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>
    /// JSONB snapshot of field values before the change.
    /// </summary>
    public string BeforeSnapshot { get; set; } = "{}";

    /// <summary>
    /// JSONB snapshot of field values after the change.
    /// </summary>
    public string AfterSnapshot { get; set; } = "{}";

    /// <summary>
    /// The user who made the change.
    /// </summary>
    public Guid? ChangedByUserId { get; set; }

    /// <summary>
    /// Email/name of the user who made the change.
    /// </summary>
    public string ChangedBy { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the change.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
