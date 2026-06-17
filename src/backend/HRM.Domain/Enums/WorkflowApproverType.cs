namespace HRM.Domain.Enums;

/// <summary>
/// How a workflow step resolves its approver(s) at runtime (US-ADM-007 FR-2).
/// </summary>
public enum WorkflowApproverType
{
    /// <summary>The requester's direct line manager (resolved per-request; no identifier needed).</summary>
    LineManager = 0,

    /// <summary>Any user holding a specific role — <c>ApproverIdentifier</c> is the role id.</summary>
    Role = 1,

    /// <summary>A specific named user — <c>ApproverIdentifier</c> is the user id.</summary>
    NamedUser = 2,

    /// <summary>The head of the requester's department (resolved per-request; no identifier needed).</summary>
    DepartmentHead = 3
}
