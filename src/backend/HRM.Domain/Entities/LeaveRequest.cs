using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A leave request submitted by an employee (US-LV-003).
/// Tenant-scoped via BaseEntity.TenantId. Created in the Pending state;
/// approval/rejection/cancellation transitions are handled by later stories
/// (US-LV-004/005). Maps to the "leave_request" table (§7).
/// </summary>
public sealed class LeaveRequest : BaseEntity
{
    /// <summary>
    /// FK to the employee applying for leave.
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// FK to the leave type being requested.
    /// </summary>
    public Guid LeaveTypeId { get; set; }

    /// <summary>
    /// First day of leave (inclusive).
    /// </summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// Last day of leave (inclusive). Equals StartDate for a single-day or half-day request.
    /// </summary>
    public DateOnly EndDate { get; set; }

    /// <summary>
    /// Whether this is a half-day request (AC-4).
    /// </summary>
    public bool IsHalfDay { get; set; }

    /// <summary>
    /// Half-day session: "AM" or "PM". Null when IsHalfDay is false (§7, AC-4).
    /// </summary>
    public string? HalfDaySession { get; set; }

    /// <summary>
    /// Computed working-day count for the request (FR-3).
    /// 0.5 for a half-day; otherwise weekdays (and, when available, non-holidays).
    /// </summary>
    public decimal TotalDays { get; set; }

    /// <summary>
    /// Free-text reason supplied by the employee (FR-1).
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Current status. Always Pending on creation in US-LV-003 (FR-6).
    /// </summary>
    public LeaveRequestStatus Status { get; set; } = LeaveRequestStatus.Pending;

    /// <summary>
    /// When the request was submitted.
    /// </summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// URLs of supporting attachments already uploaded to tenant-scoped storage (§7, NFR-3).
    /// Stored as a PostgreSQL text[] column. The actual blob upload is out of scope for
    /// US-LV-003 (deferred to frontend/storage); only the URLs are persisted here.
    /// </summary>
    public List<string> AttachmentUrls { get; set; } = new();

    /// <summary>
    /// Optimistic-concurrency token (US-LV-005 FR-6, AC-5, NFR-4). A <see cref="uint"/> rowversion
    /// property that the Npgsql model-finalizing convention maps to the PostgreSQL <c>xmin</c>
    /// system column — it requires NO migration DDL (xmin exists on every table). Concurrent
    /// approve/reject attempts on the same request cause EF to throw
    /// <c>DbUpdateConcurrencyException</c>, which the approval service translates to a clean 409.
    /// </summary>
    public uint Version { get; set; }

    /// <summary>
    /// When the request was cancelled by the employee (US-LV-010 §7, FR-2/FR-3). Null until
    /// the request is cancelled. Set on both pending and approved cancellations.
    /// </summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Free-text reason supplied by the employee when cancelling (US-LV-010 §7, BR-5).
    /// Mandatory for approved cancellations; optional for pending ones. Null until cancelled.
    /// </summary>
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Whether this request is a Loss-of-Pay (LOP) entry (US-LV-011 §7, FR-4). When true, no balance
    /// is deducted (BR-1); the record exists purely so payroll can compute the unpaid-day deduction.
    /// Default false for ordinary leave.
    /// </summary>
    public bool IsLop { get; set; }

    /// <summary>
    /// Origin of the LOP entry (US-LV-011 §7, FR-4). Null on a normal (non-LOP) request. One of
    /// EmployeeRequest / SystemGenerated / HrAssigned / Compulsory. Stored as a string.
    /// </summary>
    public LopSource? LopSource { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    public Employee? Employee { get; set; }
    public LeaveType? LeaveType { get; set; }
}
