namespace HRM.Domain.Enums;

/// <summary>
/// The decision state of a single live workflow STEP instance (US-ADM-011 FR-2). A step-instance is created
/// either as <see cref="Pending"/> (the active step awaiting its approver) or <see cref="Skipped"/> (its
/// condition was not satisfied — §13 Q4 records skips as materialized rows so the chain/history is complete).
/// </summary>
public enum WorkflowStepDecision
{
    /// <summary>Awaiting the resolved approver's decision (the active step).</summary>
    Pending = 0,

    /// <summary>The approver approved this step; the instance advances to the next applicable step.</summary>
    Approved = 1,

    /// <summary>The approver rejected this step; the instance (and request) terminate as Rejected (BR-2).</summary>
    Rejected = 2,

    /// <summary>The step's condition was not satisfied — never required an approver (BR-3, §13 Q4).</summary>
    Skipped = 3,

    /// <summary>The step's SLA elapsed and it was escalated (reserved for US-ADM-011b).</summary>
    Escalated = 4,

    /// <summary>The step was routed to a delegate approver (reserved for US-ADM-011c).</summary>
    Delegated = 5
}
