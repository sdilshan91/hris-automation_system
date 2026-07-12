using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A tenant-scoped enrollment of an employee in a <see cref="TrainingCourse"/> (US-TRN-001 FR-2). One ACTIVE
/// enrollment (<see cref="EnrollmentStatus.Enrolled"/>/<see cref="EnrollmentStatus.Waitlisted"/>) per
/// (course, employee) is enforced by a partial unique index (BR-2). Waitlisted rows carry a FIFO
/// <see cref="WaitlistPosition"/>; cancelling an <see cref="EnrollmentStatus.Enrolled"/> seat promotes the
/// head of the waitlist (BR-3).
/// </summary>
public sealed class CourseEnrollment : BaseEntity, IAuditExempt
{
    /// <summary>FK → <see cref="TrainingCourse"/> (FR-2).</summary>
    public Guid CourseId { get; set; }

    /// <summary>FK → Employee — the enrollee (FR-2).</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>Enrollment status (FR-2).</summary>
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Enrolled;

    /// <summary>When the enrollment was created (AC-4).</summary>
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;

    /// <summary>The user who created the enrollment (self-enroll or an HR admin) (AC-4).</summary>
    public Guid EnrolledBy { get; set; }

    /// <summary>FIFO position on the waitlist; null when not waitlisted (AC-5, BR-3).</summary>
    public int? WaitlistPosition { get; set; }

    /// <summary>When the enrollment was cancelled (AC-7).</summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>When completion was marked (AC-8).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Optional certificate reference / URL captured at completion (AC-8).</summary>
    public string? CertificateReference { get; set; }

    /// <summary>Optional completion score (AC-8).</summary>
    public decimal? Score { get; set; }
}
