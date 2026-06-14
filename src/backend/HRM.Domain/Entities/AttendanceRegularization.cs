namespace HRM.Domain.Entities;

/// <summary>
/// An employee-submitted request to correct a missed clock-in and/or clock-out for a past working
/// day (US-ATT-003 §7). Created with status "PENDING"; manager approve/reject is US-ATT-004 and is
/// the step that actually mutates the underlying <see cref="AttendanceLog"/> (BR-5) — submission does
/// NOT touch the log. Tenant-scoped via <see cref="BaseEntity.TenantId"/> and the EF global query
/// filter. Maps to the "attendance_regularization" table.
/// </summary>
public sealed class AttendanceRegularization : BaseEntity
{
    /// <summary>
    /// FK to the employee the regularization belongs to (FR-2).
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// FK to an existing <see cref="AttendanceLog"/> for the regularized date, when one exists
    /// (e.g. clocked in but forgot to clock out, AC-2/BR-5). Null when no record exists for the date
    /// (missed both, AC-1) — a new log is created on approval (US-ATT-004), not now.
    /// </summary>
    public Guid? AttendanceLogId { get; set; }

    /// <summary>
    /// The calendar date being regularized (FR-2). Stored as a date (no time component).
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Which punch(es) the request corrects (FR-1/§7):
    /// "MISSED_CLOCK_IN", "MISSED_CLOCK_OUT", or "MISSED_BOTH".
    /// </summary>
    public string RegularizationType { get; set; } = default!;

    /// <summary>
    /// The requested corrected clock-in instant in UTC (§7). Required when the type is
    /// MISSED_CLOCK_IN or MISSED_BOTH; null otherwise.
    /// </summary>
    public DateTime? RequestedClockIn { get; set; }

    /// <summary>
    /// The requested corrected clock-out instant in UTC (§7). Required when the type is
    /// MISSED_CLOCK_OUT or MISSED_BOTH; null otherwise.
    /// </summary>
    public DateTime? RequestedClockOut { get; set; }

    /// <summary>
    /// Mandatory free-text justification, minimum 10 characters (BR-7).
    /// </summary>
    public string Reason { get; set; } = default!;

    /// <summary>
    /// Workflow state (§7): "PENDING" (on submit), "APPROVED" / "REJECTED" (US-ATT-004),
    /// or "CANCELLED" (employee withdraws — future).
    /// </summary>
    public string Status { get; set; } = "PENDING";

    /// <summary>
    /// FK to the approval-workflow instance once an Approval Workflow Engine exists (FR-3/AC-1).
    /// No workflow engine is built yet (US-ADM-007); this is left null on submit. TODO(US-ADM-007):
    /// wire to the real engine and populate on submission.
    /// </summary>
    public Guid? WorkflowInstanceId { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    public Employee? Employee { get; set; }

    public AttendanceLog? AttendanceLog { get; set; }
}
