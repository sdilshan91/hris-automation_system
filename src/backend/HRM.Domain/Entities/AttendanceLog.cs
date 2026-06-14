namespace HRM.Domain.Entities;

/// <summary>
/// A single attendance punch record for an employee (US-ATT-001 §7).
/// Created on clock-in with <see cref="ClockOut"/> left null until the employee clocks out
/// (a later story). Tenant-scoped via <see cref="BaseEntity.TenantId"/> and the EF global
/// query filter. Maps to the "attendance_log" table.
/// </summary>
public sealed class AttendanceLog : BaseEntity
{
    /// <summary>
    /// FK to the employee the punch belongs to (FR-1).
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// Clock-in instant, always stored in UTC (FR-1, FR-7, AC-1).
    /// </summary>
    public DateTime ClockIn { get; set; }

    /// <summary>
    /// Clock-out instant in UTC. Null while the record is still open (BR-1).
    /// Set by a later clock-out story.
    /// </summary>
    public DateTime? ClockOut { get; set; }

    /// <summary>
    /// Captured latitude at clock-in (FR-1, AC-3/AC-4). Null when geolocation was not provided.
    /// </summary>
    public decimal? ClockInLatitude { get; set; }

    /// <summary>
    /// Captured longitude at clock-in (FR-1, AC-3/AC-4). Null when geolocation was not provided.
    /// </summary>
    public decimal? ClockInLongitude { get; set; }

    /// <summary>
    /// Source IP address recorded for audit (FR-5).
    /// </summary>
    public string? ClockInIp { get; set; }

    /// <summary>
    /// Browser user-agent recorded for audit (FR-5).
    /// </summary>
    public string? ClockInUserAgent { get; set; }

    /// <summary>
    /// URL of the selfie photo captured at clock-in when the tenant requires it (BR-6).
    /// Null when photo capture is not required. The actual blob upload is out of scope;
    /// only the resolved URL is persisted here.
    /// </summary>
    public string? ClockInPhotoUrl { get; set; }

    /// <summary>
    /// Origin of the punch: "WEB" or "MOBILE_WEB" (§7). Native mobile is out of scope (Phase 1).
    /// </summary>
    public string Source { get; set; } = "WEB";

    // ── Navigation ─────────────────────────────────────────────────

    public Employee? Employee { get; set; }
}
