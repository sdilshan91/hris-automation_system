namespace HRM.Application.Features.Training.DTOs;

/// <summary>
/// A training course with computed enrollment/waitlist counts (US-TRN-001). Enum fields are serialized as their
/// string names to keep the FE contract stable.
/// </summary>
public sealed record CourseDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Mode { get; init; } = string.Empty;
    public int? Capacity { get; init; }
    public string? Location { get; init; }
    public string? Instructor { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public decimal? DurationHours { get; init; }
    public string Status { get; init; } = string.Empty;
    public int EnrolledCount { get; init; }
    public int WaitlistCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>Request body for creating a course (creates it in <c>Draft</c>) (AC-1).</summary>
public sealed record CreateCourseRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Mode { get; init; } = "InPerson";
    public int? Capacity { get; init; }
    public string? Location { get; init; }
    public string? Instructor { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public decimal? DurationHours { get; init; }
}

/// <summary>Request body for updating course metadata (not status) (FR-3).</summary>
public sealed record UpdateCourseRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Mode { get; init; } = "InPerson";
    public int? Capacity { get; init; }
    public string? Location { get; init; }
    public string? Instructor { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public decimal? DurationHours { get; init; }
}

/// <summary>Request body for a course status transition (AC-2).</summary>
public sealed record ChangeCourseStatusRequest
{
    public string Status { get; init; } = string.Empty;
}

/// <summary>Request body for enrolling. Null <see cref="EmployeeId"/> = self-enroll the current user's employee
/// (ViewOwn); a non-null value enrolls another employee and requires Manage (AC-4).</summary>
public sealed record EnrollRequest
{
    public Guid? EmployeeId { get; init; }
}

/// <summary>Request body for marking an enrollment completed (AC-8).</summary>
public sealed record CompleteEnrollmentRequest
{
    public string? CertificateReference { get; init; }
    public decimal? Score { get; init; }
}

/// <summary>An enrollment with its course title + employee name resolved for display (US-TRN-001).</summary>
public sealed record EnrollmentDto
{
    public Guid Id { get; init; }
    public Guid CourseId { get; init; }
    public string CourseTitle { get; init; } = string.Empty;
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int? WaitlistPosition { get; init; }
    public DateTime EnrolledAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? CertificateReference { get; init; }
    public decimal? Score { get; init; }
}
