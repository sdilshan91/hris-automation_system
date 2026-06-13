namespace HRM.Domain.Enums;

/// <summary>
/// The action a manager (or higher-level approver) took on a leave request (US-LV-005 §7).
/// Stored as a string in the database, matching the leave enum convention.
/// </summary>
public enum LeaveApprovalAction
{
    Approved = 0,
    Rejected = 1,
}
