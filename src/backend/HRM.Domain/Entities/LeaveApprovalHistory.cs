using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// An immutable record of an approval/rejection decision taken on a leave request (US-LV-005 §7, FR-5).
/// One row is written per decision. With single-level approval (the only level implemented in this
/// story) there is exactly one row per actioned request, always at <see cref="ApprovalLevel"/> 1.
/// Multi-level routing (FR-5/AC-4) is deferred to US-ADM-007 — see the vault note.
/// Maps to the "leave_approval_history" table. Tenant-scoped via BaseEntity.TenantId.
/// </summary>
public sealed class LeaveApprovalHistory : BaseEntity
{
    /// <summary>
    /// FK to the leave request this decision applies to.
    /// </summary>
    public Guid LeaveRequestId { get; set; }

    /// <summary>
    /// FK to the employee record of the approver who took the action.
    /// </summary>
    public Guid ApproverEmployeeId { get; set; }

    /// <summary>
    /// Approval level (1-based). Always 1 in this story (single-level direct-report approval).
    /// </summary>
    public int ApprovalLevel { get; set; } = 1;

    /// <summary>
    /// Whether the request was approved or rejected.
    /// </summary>
    public LeaveApprovalAction Action { get; set; }

    /// <summary>
    /// Optional comment (approval) or mandatory reason (rejection, BR-2).
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// When the decision was taken.
    /// </summary>
    public DateTime ActionedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ─────────────────────────────────────────────────

    public LeaveRequest? LeaveRequest { get; set; }
    public Employee? Approver { get; set; }
}
