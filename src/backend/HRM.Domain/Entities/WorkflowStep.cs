using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A single ordered step in a <see cref="WorkflowDefinition"/> (US-ADM-007 FR-2). Stored as a child-table row
/// (tenant-scoped, inherits <see cref="BaseEntity"/>) keyed by <see cref="WorkflowDefinitionId"/>.
/// </summary>
public sealed class WorkflowStep : BaseEntity
{
    /// <summary>Owning workflow-definition row (a specific version).</summary>
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>1-based ordinal that determines evaluation order (FR-2).</summary>
    public int StepOrder { get; set; }

    /// <summary>How the approver(s) for this step are resolved (FR-2).</summary>
    public WorkflowApproverType ApproverType { get; set; }

    /// <summary>
    /// Role id (when <see cref="ApproverType"/> is <see cref="WorkflowApproverType.Role"/>) or user id (when
    /// <see cref="WorkflowApproverType.NamedUser"/>). Null for LineManager / DepartmentHead (resolved per-request).
    /// </summary>
    public Guid? ApproverIdentifier { get; set; }

    /// <summary>
    /// True when this step is a parallel-approval step (BR-3) — all approvers must approve before proceeding.
    /// The pure evaluator surfaces such steps as an all-approvers-required group.
    /// </summary>
    public bool IsParallel { get; set; }

    /// <summary>SLA in hours before this step is considered breached (FR-2). Must be a positive integer (FR-5).</summary>
    public int SlaHours { get; set; }

    /// <summary>Approver type to escalate to on SLA breach (BR-4). Null = no explicit escalation target.</summary>
    public WorkflowApproverType? EscalationApproverType { get; set; }

    /// <summary>Role/user id for the escalation target, when applicable. Null for LineManager / DepartmentHead.</summary>
    public Guid? EscalationApproverIdentifier { get; set; }

    /// <summary>
    /// Optional simple condition JSON (BR-5), e.g. <c>{"field":"days_requested","operator":"&gt;","value":5}</c>.
    /// When set and not satisfied by a request's data, the evaluator SKIPS this step. Null = always applicable.
    /// </summary>
    public string? ConditionJson { get; set; }

    /// <summary>True when delegation is configured for this step (AC-5). The runtime routing is deferred.</summary>
    public bool DelegationEnabled { get; set; }

    /// <summary>The backup user to route to when delegation fires (AC-5). Config only; runtime routing deferred.</summary>
    public Guid? DelegationBackupUserId { get; set; }

    /// <summary>
    /// US-ADM-011b: ADDITIONAL approvers for a parallel step (beyond the step's own primary approver #1). Empty
    /// for sequential steps. The runtime fans out one <see cref="WorkflowStepInstance"/> per distinct resolved
    /// approver (primary + these), joined all-approve/any-reject (AC-4).
    /// </summary>
    public ICollection<WorkflowStepApprover> Approvers { get; set; } = new List<WorkflowStepApprover>();
}
