namespace HRM.Domain.Enums;

/// <summary>
/// Status of a course enrollment (US-TRN-001 FR-2). Stored as a string column.
/// <see cref="Enrolled"/> and <see cref="Waitlisted"/> are the two ACTIVE states (BR-2: one active enrollment
/// per employee per course).
/// </summary>
public enum EnrollmentStatus
{
    Enrolled = 0,
    Waitlisted = 1,
    Cancelled = 2,
    Completed = 3,
    NoShow = 4,
}
