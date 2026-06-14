namespace HRM.Domain.Entities;

/// <summary>
/// An immutable record of an approve/reject decision taken on an <see cref="OvertimeRecord"/>
/// (US-ATT-006 FR-6/NFR-3). One row is written per decision. With single-level approval — the only
/// level implemented (no Approval Workflow Engine, US-ADM-007) — there is exactly one row per actioned
/// overtime record, always at <see cref="ApprovalLevel"/> 1. Mirrors the established
/// <see cref="RegularizationApprovalHistory"/> pattern (US-ATT-004). Tenant-scoped via
/// <see cref="BaseEntity.TenantId"/> + the EF global query filter. Maps to the
/// "overtime_approval_history" table.
///
/// NFR-3 (immutable + auditable): rows are only ever inserted, never updated or deleted, by the approval
/// service. The decision also captures the approved minutes and the calculation basis snapshot so the
/// full decision trail is reconstructable.
/// </summary>
public sealed class OvertimeApprovalHistory : BaseEntity
{
    /// <summary>FK to the <see cref="OvertimeRecord"/> this decision applies to.</summary>
    public Guid OvertimeRecordId { get; set; }

    /// <summary>FK to the employee record of the approver (manager) who took the action (FR-6).</summary>
    public Guid ApproverEmployeeId { get; set; }

    /// <summary>Approval level (1-based). Always 1 (single-level direct-report approval).</summary>
    public int ApprovalLevel { get; set; } = 1;

    /// <summary>The decision: "APPROVED" or "REJECTED" (US-ATT-006 §7).</summary>
    public string Action { get; set; } = default!;

    /// <summary>
    /// Minutes approved on an approve action (defaults to the requested minutes, or the manager's
    /// adjusted value, FR-6/AC-4). Null on rejection.
    /// </summary>
    public int? ApprovedMinutes { get; set; }

    /// <summary>Optional comment on approval; mandatory reason on rejection (min 10 chars).</summary>
    public string? Comment { get; set; }

    /// <summary>
    /// NFR-3: the calculation-basis snapshot at decision time (formula inputs that produced the
    /// overtime minutes + multiplier), copied from the record so the audit trail is self-contained.
    /// </summary>
    public string? CalculationBasis { get; set; }

    /// <summary>When the decision was taken (FR-6).</summary>
    public DateTime ActionedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ─────────────────────────────────────────────────

    public OvertimeRecord? OvertimeRecord { get; set; }

    public Employee? Approver { get; set; }
}

/// <summary>
/// The allowed values for <see cref="OvertimeApprovalHistory.Action"/> (US-ATT-006 §7).
/// </summary>
public static class OvertimeApprovalAction
{
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
}
