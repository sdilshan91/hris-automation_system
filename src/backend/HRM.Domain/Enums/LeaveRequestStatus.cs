namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a leave request (US-LV-003 §7).
/// Stored as a string in the database to match the leave enum convention.
/// US-LV-003 only ever creates requests in the Pending state; the remaining
/// states are populated by the approval/cancellation flows (US-LV-004/005).
/// </summary>
public enum LeaveRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
}
