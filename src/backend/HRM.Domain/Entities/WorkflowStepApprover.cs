using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// US-ADM-011b: an ADDITIONAL approver on a parallel <see cref="WorkflowStep"/> (beyond the step's own
/// primary approver #1). Tenant-scoped child row. For a parallel step, the runtime fans out one
/// WorkflowStepInstance per distinct resolved approver (primary + these), joined all-approve/any-reject (AC-4).
/// </summary>
public sealed class WorkflowStepApprover : BaseEntity
{
    public Guid WorkflowStepId { get; set; }
    public WorkflowApproverType ApproverType { get; set; }
    /// <summary>User id (NamedUser) or role id (Role); null for LineManager/DepartmentHead. FE sends NamedUser ids.</summary>
    public Guid? ApproverIdentifier { get; set; }
}
