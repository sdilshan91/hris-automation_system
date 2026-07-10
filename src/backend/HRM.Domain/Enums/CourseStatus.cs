namespace HRM.Domain.Enums;

/// <summary>
/// Lifecycle status of a training course (US-TRN-001 FR-1). Stored as a string column.
/// Only <see cref="Open"/> courses are enrollable (BR-1). Legal transitions are enforced by the service:
/// Draft → Open/Cancelled; Open → Closed/Cancelled/Completed; Closed → Completed/Cancelled;
/// Cancelled/Completed are terminal.
/// </summary>
public enum CourseStatus
{
    Draft = 0,
    Open = 1,
    Closed = 2,
    Cancelled = 3,
    Completed = 4,
}
