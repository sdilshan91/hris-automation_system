namespace HRM.Domain.Enums;

/// <summary>
/// The action a manager (or higher-level approver) took on a leave request (US-LV-005 §7),
/// or an employee self-cancellation (US-LV-010 FR-6). Stored as a string in the database,
/// matching the leave enum convention (the column is varchar(20), so adding a value needs no
/// schema change).
/// </summary>
public enum LeaveApprovalAction
{
    Approved = 0,
    Rejected = 1,
    /// <summary>The requesting employee cancelled their own leave request (US-LV-010 FR-6).</summary>
    Cancelled = 2,
}
