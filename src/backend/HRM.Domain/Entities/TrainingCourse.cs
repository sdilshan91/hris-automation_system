using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A tenant-scoped training course in the catalog (US-TRN-001 FR-1). Employees enroll via
/// <see cref="CourseEnrollment"/>. Capacity is optional (null = unlimited, never waitlists — BR-5).
/// </summary>
public sealed class TrainingCourse : BaseEntity
{
    /// <summary>Course title (required) (FR-1, AC-1).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional free-text description of the course.</summary>
    public string? Description { get; set; }

    /// <summary>Delivery mode: in-person / online / hybrid (FR-1).</summary>
    public TrainingMode Mode { get; set; } = TrainingMode.InPerson;

    /// <summary>Seat capacity. Null = unlimited (never waitlists) (BR-5).</summary>
    public int? Capacity { get; set; }

    /// <summary>Optional location / meeting link.</summary>
    public string? Location { get; set; }

    /// <summary>Optional instructor / facilitator name.</summary>
    public string? Instructor { get; set; }

    /// <summary>Optional scheduled start date.</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>Optional scheduled end date (must be &gt;= StartDate when both set) (AC-1).</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>Optional total duration in hours.</summary>
    public decimal? DurationHours { get; set; }

    /// <summary>Lifecycle status. Only <see cref="CourseStatus.Open"/> courses are enrollable (BR-1).</summary>
    public CourseStatus Status { get; set; } = CourseStatus.Draft;
}
