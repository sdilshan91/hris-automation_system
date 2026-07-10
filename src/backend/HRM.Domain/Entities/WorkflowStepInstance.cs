using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A single live STEP within a <see cref="WorkflowInstance"/> (US-ADM-011 FR-2). Created either as the active
/// <see cref="WorkflowStepDecision.Pending"/> step or as a materialized <see cref="WorkflowStepDecision.Skipped"/>
/// row (§13 Q4 — condition unmet). Tenant-scoped via <see cref="BaseEntity"/> (RLS-eligible).
/// </summary>
public sealed class WorkflowStepInstance : BaseEntity
{
    /// <summary>Owning <see cref="WorkflowInstance"/>.</summary>
    public Guid WorkflowInstanceId { get; set; }

    /// <summary>The <see cref="WorkflowStep.StepOrder"/> this instance was created from (FR-2).</summary>
    public int StepOrder { get; set; }

    /// <summary>True when the source step was a parallel step (relevant to US-ADM-011b join logic).</summary>
    public bool IsParallel { get; set; }

    /// <summary>How this step's approver is resolved (FR-2) — copied from the definition step.</summary>
    public WorkflowApproverType ApproverType { get; set; }

    /// <summary>
    /// The role/user id from the definition step (for <c>Role</c>/<c>NamedUser</c> authorization). Null for
    /// <c>LineManager</c>/<c>DepartmentHead</c> (resolved per-request to <see cref="AssignedApproverUserId"/>).
    /// </summary>
    public Guid? ApproverIdentifier { get; set; }

    /// <summary>
    /// The concrete resolved approver USER id (FR-5). Set for LineManager/DepartmentHead/NamedUser at activation;
    /// null for a <c>Role</c> step (any holder of the role may decide — authorization checks role membership).
    /// </summary>
    public Guid? AssignedApproverUserId { get; set; }

    /// <summary>Decision state of this step (FR-2).</summary>
    public WorkflowStepDecision Decision { get; set; } = WorkflowStepDecision.Pending;

    /// <summary>The user who decided this step (approve/reject). Null until decided.</summary>
    public Guid? DecidedByUserId { get; set; }

    /// <summary>When this step was decided. Null until decided.</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>Optional decision comment supplied by the approver.</summary>
    public string? Comments { get; set; }

    /// <summary>
    /// When this step's SLA elapses (= activation time + <see cref="WorkflowStep.SlaHours"/>) — FR-2. Stamped now
    /// so the SLA-timer job (US-ADM-011b) can scan for breaches; the job itself is out of Phase-1 scope.
    /// </summary>
    public DateTime? SlaDueAt { get; set; }

    /// <summary>When the step was auto-escalated (reserved for US-ADM-011b). Idempotency guard for the SLA job.</summary>
    public DateTime? EscalatedAt { get; set; }
}
