namespace HRM.Domain.Entities;

/// <summary>
/// Allowed values for <see cref="OvertimeRecord.Type"/> (US-ATT-006 §7/FR-2).
/// </summary>
public static class OvertimeType
{
    /// <summary>Created automatically as part of clock-out when work exceeded standard + threshold (FR-1).</summary>
    public const string AutoDetected = "AUTO_DETECTED";

    /// <summary>Created from an employee-submitted pre-approval request before working (FR-4/AC-2).</summary>
    public const string PreApproved = "PRE_APPROVED";

    public static bool IsValid(string type) => type is AutoDetected or PreApproved;
}

/// <summary>
/// Allowed values for <see cref="OvertimeRecord.Status"/> (US-ATT-006 §7).
/// </summary>
public static class OvertimeStatus
{
    /// <summary>Awaiting manager approval (default for auto-detected / submitted overtime).</summary>
    public const string Pending = "PENDING";

    /// <summary>Approved by the manager — flagged payroll-ready (AC-4/FR-7).</summary>
    public const string Approved = "APPROVED";

    /// <summary>Rejected by the manager.</summary>
    public const string Rejected = "REJECTED";

    /// <summary>
    /// Worked overtime that has no matching pre-approval while the tenant requires pre-approval
    /// (BR-6). Recorded but excluded from payroll until HR reviews it.
    /// </summary>
    public const string Unapproved = "UNAPPROVED";

    public static bool IsValid(string status) =>
        status is Pending or Approved or Rejected or Unapproved;
}

/// <summary>
/// A tracked block of overtime worked by an employee on a single day (US-ATT-006 §7). Created either
/// automatically as part of the clock-out transaction when net work exceeds the shift standard plus the
/// configured threshold (FR-1/AC-1/NFR-1), or from an employee pre-approval request (FR-4/AC-2).
/// Routed to the employee's manager for single-level approval (FR-5/FR-6 — no Approval Workflow Engine
/// exists yet, US-ADM-007, so <see cref="WorkflowInstanceId"/> is left null). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> and the EF global query filter. Maps to the "overtime_record" table.
/// </summary>
public sealed class OvertimeRecord : BaseEntity
{
    /// <summary>FK to the employee the overtime belongs to (FR-2).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// FK to the <see cref="AttendanceLog"/> the overtime was auto-detected from, when applicable
    /// (AC-1). Null for a pre-approval request submitted before any clock-out (AC-2).
    /// </summary>
    public Guid? AttendanceLogId { get; set; }

    /// <summary>The overtime date (FR-2). Stored as a date with no time component.</summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Actual overtime minutes detected/requested (FR-2). For auto-detected records this is the net
    /// excess after applying the daily cap (BR-4); for pre-approval it is the employee's expected
    /// minutes. The deterministic inputs that produced this value are recorded in
    /// <see cref="CalculationBasis"/> (NFR-3).
    /// </summary>
    public int OvertimeMinutes { get; set; }

    /// <summary>
    /// Minutes the manager approved (§7). Null until approved; defaults to <see cref="OvertimeMinutes"/>
    /// unless the manager adjusts it down (FR-6/AC-4).
    /// </summary>
    public int? ApprovedMinutes { get; set; }

    /// <summary>
    /// Pay multiplier applicable to this overtime (FR-3/BR-3/BR-7): weekday (1.5x), weekend (2.0x), or
    /// public holiday (2.5x), per the tenant overtime rules. Applied during payroll, not here (§10).
    /// </summary>
    public decimal Multiplier { get; set; }

    /// <summary>One of <see cref="OvertimeType"/> (FR-2).</summary>
    public string Type { get; set; } = OvertimeType.AutoDetected;

    /// <summary>One of <see cref="OvertimeStatus"/> (§7).</summary>
    public string Status { get; set; } = OvertimeStatus.Pending;

    /// <summary>Employee- or system-supplied reason/justification (FR-2/§7).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Optional manager comment recorded on approve/reject (§7).</summary>
    public string? ManagerComment { get; set; }

    /// <summary>
    /// True once the overtime is approved and ready for the payroll module to consume (FR-7/AC-4).
    /// A placeholder boolean until the Payroll module (US-ATT-009/US-PAY) owns the integration; UNAPPROVED
    /// overtime is never payroll-ready (BR-6).
    /// </summary>
    public bool IsPayrollReady { get; set; }

    /// <summary>
    /// True when <see cref="OvertimeMinutes"/> was reduced by the daily cap (BR-4/FR-8) — surfaced so HR
    /// can be alerted that the worked overtime exceeded the tenant maximum.
    /// </summary>
    public bool DailyCapApplied { get; set; }

    /// <summary>
    /// True when this record pushed the employee's weekly approved+pending overtime over the configured
    /// weekly maximum (BR-5/FR-8) — surfaced as an HR alert flag.
    /// </summary>
    public bool WeeklyCapExceeded { get; set; }

    /// <summary>
    /// NFR-3 (deterministic + auditable): a human-readable record of the exact formula inputs that
    /// produced <see cref="OvertimeMinutes"/> and <see cref="Multiplier"/> — standard minutes, threshold,
    /// raw net excess, cap applied, and the multiplier basis (weekday/weekend/holiday). Persisted so the
    /// calculation can be reconstructed without re-deriving from possibly-changed settings.
    /// </summary>
    public string? CalculationBasis { get; set; }

    /// <summary>
    /// FK to the approval-workflow instance once an Approval Workflow Engine exists (FR-5). No engine is
    /// built yet (US-ADM-007); left null. TODO(US-ADM-007): wire to the real engine.
    /// </summary>
    public Guid? WorkflowInstanceId { get; set; }

    // ── Navigation ─────────────────────────────────────────────────

    public Employee? Employee { get; set; }

    public AttendanceLog? AttendanceLog { get; set; }
}
