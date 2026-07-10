namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a live approval-workflow INSTANCE (US-ADM-011 FR-1). One instance is created per
/// submitted request that matches an Active <see cref="WorkflowEntityType"/> definition.
/// </summary>
public enum WorkflowInstanceStatus
{
    /// <summary>At least one step is still Pending — the request is routing through the workflow.</summary>
    InProgress = 0,

    /// <summary>All applicable steps approved — the underlying request has been approved (BR-2).</summary>
    Approved = 1,

    /// <summary>A step (or a parallel approver) rejected — the instance and its request are rejected (BR-2).</summary>
    Rejected = 2,

    /// <summary>The instance was escalated past its final step with no resolution (reserved for US-ADM-011b).</summary>
    Escalated = 3,

    /// <summary>The underlying request was cancelled before the workflow completed.</summary>
    Cancelled = 4
}
