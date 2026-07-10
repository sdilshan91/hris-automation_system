using HRM.Application.Common.Models;
using HRM.Application.Features.Training.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Training catalog + enrollment service (US-TRN-001). All operations are tenant-scoped via
/// <see cref="ITenantContext"/> and the EF global query filters. Catalog visibility, capacity/waitlist,
/// FIFO promotion, completion and per-employee history live here; thin MediatR handlers delegate to it.
/// </summary>
public interface ITrainingService
{
    /// <summary>Lists courses. Callers without View.All/Manage see only <c>Open</c> courses (AC-3).</summary>
    Task<Result<IReadOnlyList<CourseDto>>> GetCoursesAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one course (same visibility rule as the list) (AC-3).</summary>
    Task<Result<CourseDto>> GetCourseByIdAsync(Guid courseId, CancellationToken cancellationToken = default);

    /// <summary>Creates a course in <c>Draft</c> (AC-1). Requires Training.Manage (enforced by the controller).</summary>
    Task<Result<CourseDto>> CreateCourseAsync(CreateCourseRequest request, CancellationToken cancellationToken = default);

    /// <summary>Updates course metadata (not status) (FR-3).</summary>
    Task<Result<CourseDto>> UpdateCourseAsync(Guid courseId, UpdateCourseRequest request, CancellationToken cancellationToken = default);

    /// <summary>Transitions course status, validating legal transitions (AC-2). Illegal → 409.</summary>
    Task<Result<CourseDto>> ChangeStatusAsync(Guid courseId, string targetStatus, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls an employee (AC-4/AC-5/AC-6). Race-safe: on Postgres the seat-claim runs inside the retry-safe
    /// execution strategy with a <c>SELECT … FOR UPDATE</c> lock on the course row (NFR-3, BUG-068 class). Null
    /// <see cref="EnrollRequest.EmployeeId"/> = self-enroll; enrolling another employee requires Training.Manage.
    /// </summary>
    Task<Result<EnrollmentDto>> EnrollAsync(Guid courseId, EnrollRequest request, CancellationToken cancellationToken = default);

    /// <summary>Cancels an enrollment; a freed seat promotes the FIFO waitlist head (AC-7, BR-3).</summary>
    Task<Result<EnrollmentDto>> CancelAsync(Guid enrollmentId, CancellationToken cancellationToken = default);

    /// <summary>Marks an enrollment completed (+ optional certificate/score) (AC-8). Requires Training.Manage.</summary>
    Task<Result<EnrollmentDto>> CompleteAsync(Guid enrollmentId, CompleteEnrollmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns the current user's employee's enrollments (AC-9).</summary>
    Task<Result<IReadOnlyList<EnrollmentDto>>> GetMyEnrollmentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns an employee's enrollment history (AC-9). Self is allowed with View.Own; another employee's history
    /// requires View.All or Manage.
    /// </summary>
    Task<Result<IReadOnlyList<EnrollmentDto>>> GetTrainingHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
