namespace HRM.Domain.Entities;

/// <summary>
/// An immutable record of an approve/reject decision taken on an attendance regularization request
/// (US-ATT-004 FR-6/NFR-4). One row is written per decision. With single-level approval — the only
/// level implemented in this story — there is exactly one row per actioned request, always at
/// <see cref="ApprovalLevel"/> 1. Multi-level routing (FR-4/AC-4) is deferred to US-ADM-007 once an
/// Approval Workflow Engine exists. Mirrors the established <see cref="LeaveApprovalHistory"/> pattern
/// (US-LV-005). Tenant-scoped via <see cref="BaseEntity.TenantId"/> + the EF global query filter.
/// Maps to the "attendance_regularization_history" table.
///
/// NFR-4 (immutable audit): rows are only ever inserted, never updated or deleted, by the approval
/// service — there is no update/delete path. The AuditInterceptor additionally stamps created_at /
/// created_by (the acting manager's user id) on insert.
/// </summary>
public sealed class RegularizationApprovalHistory : BaseEntity
{
    /// <summary>
    /// FK to the <see cref="AttendanceRegularization"/> this decision applies to.
    /// </summary>
    public Guid RegularizationId { get; set; }

    /// <summary>
    /// FK to the employee record of the approver (manager) who took the action (FR-6).
    /// </summary>
    public Guid ApproverEmployeeId { get; set; }

    /// <summary>
    /// Approval level (1-based). Always 1 in this story (single-level direct-report approval).
    /// </summary>
    public int ApprovalLevel { get; set; } = 1;

    /// <summary>
    /// The decision: "APPROVED" or "REJECTED" (US-ATT-004 §7).
    /// </summary>
    public string Action { get; set; } = default!;

    /// <summary>
    /// Optional comment on approval (BR-2); mandatory reason on rejection (BR-1, min 10 chars).
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// When the decision was taken (FR-6).
    /// </summary>
    public DateTime ActionedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ─────────────────────────────────────────────────

    public AttendanceRegularization? Regularization { get; set; }

    public Employee? Approver { get; set; }
}

/// <summary>
/// The allowed values for <see cref="RegularizationApprovalHistory.Action"/> (US-ATT-004 §7).
/// </summary>
public static class RegularizationApprovalAction
{
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
}
