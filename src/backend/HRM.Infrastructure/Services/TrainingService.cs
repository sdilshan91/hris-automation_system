using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Training.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Training catalog + enrollment service (US-TRN-001). All queries are tenant-scoped via
/// <see cref="ITenantContext"/> and the EF global query filters. The seat-claim (enroll) and cancel-with-FIFO-
/// promotion paths are race-safe (NFR-3, BUG-068 class): on a relational provider they run inside the Npgsql
/// retry-safe execution strategy wrapping a transaction that takes a pessimistic <c>SELECT … FOR UPDATE</c> lock
/// on the course row, so concurrent enrolls into the same course serialize and never oversubscribe past
/// <c>Capacity</c>. On InMemory (unit tests) the body runs directly.
/// </summary>
public sealed class TrainingService : ITrainingService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<TrainingService> _logger;

    // US-NTF-006: notification intents accumulated during a mutation and FLUSHED only AFTER the state is
    // persisted/committed (a retry of the execution strategy would otherwise double-send). Optional dispatcher so
    // the unit/Postgres harnesses that construct the service directly need no notification wiring.
    private readonly INotificationDispatcher? _dispatcher;
    private readonly List<PendingNotification> _pending = new();

    private sealed record PendingNotification(
        Guid? UserId, string EventKey, string CourseTitle, string EmployeeFirst, string EmployeeLast, string Status);

    public TrainingService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<TrainingService> logger,
        INotificationDispatcher? dispatcher = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
        _dispatcher = dispatcher;
    }

    private bool HasViewAll =>
        _currentUser.Permissions.Contains(PermissionCatalog.Training.ViewAll)
        || _currentUser.Permissions.Contains(PermissionCatalog.Training.Manage);

    private bool HasManage => _currentUser.Permissions.Contains(PermissionCatalog.Training.Manage);

    // ── Catalog reads (AC-3) ────────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<CourseDto>>> GetCoursesAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<CourseDto>>.Failure("Tenant context is not resolved.", 400);

        var query = _db.TrainingCourses.AsNoTracking();

        // AC-3: callers without View.All/Manage see only Open courses.
        if (!HasViewAll)
            query = query.Where(c => c.Status == CourseStatus.Open);

        var courses = await query.OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        var counts = await LoadCountsAsync(courses.Select(c => c.Id).ToList(), cancellationToken);

        var dtos = courses.Select(c => ToCourseDto(c, counts)).ToList();
        return Result<IReadOnlyList<CourseDto>>.Success(dtos);
    }

    public async Task<Result<CourseDto>> GetCourseByIdAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CourseDto>.Failure("Tenant context is not resolved.", 400);

        var course = await _db.TrainingCourses.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken);

        // AC-3: hide non-Open courses from callers without View.All/Manage (404, don't reveal existence).
        if (course is null || (!HasViewAll && course.Status != CourseStatus.Open))
            return Result<CourseDto>.Failure("Course not found.", 404);

        var counts = await LoadCountsAsync([course.Id], cancellationToken);
        return Result<CourseDto>.Success(ToCourseDto(course, counts));
    }

    // ── Catalog writes (Manage-gated by the controller) ─────────────────────────

    public async Task<Result<CourseDto>> CreateCourseAsync(CreateCourseRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CourseDto>.Failure("Tenant context is not resolved.", 400);

        if (string.IsNullOrWhiteSpace(request.Title))
            return Result<CourseDto>.Failure("Course title is required.", 400, "validation_error");

        if (!Enum.TryParse<TrainingMode>(request.Mode, true, out var mode))
            return Result<CourseDto>.Failure("Invalid training mode.", 400, "validation_error");

        if (request.Capacity is < 0)
            return Result<CourseDto>.Failure("Capacity must be zero or a positive number.", 400, "validation_error");

        if (request.StartDate is { } sd && request.EndDate is { } ed && ed < sd)
            return Result<CourseDto>.Failure("End date must be on or after the start date.", 400, "validation_error");

        var course = new TrainingCourse
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Mode = mode,
            Capacity = request.Capacity,
            Location = request.Location?.Trim(),
            Instructor = request.Instructor?.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            DurationHours = request.DurationHours,
            Status = CourseStatus.Draft,
            IsDeleted = false,
        };

        _db.TrainingCourses.Add(course);
        AddAudit("Training.CourseCreated", "TrainingCourse", course.Id, $"Course '{course.Title}' created (Draft).");
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Training course created. Id={CourseId}, TenantId={TenantId}, By={User}",
            course.Id, _tenantContext.TenantId, _currentUser.Email);

        return Result<CourseDto>.Success(ToCourseDto(course, EmptyCounts));
    }

    public async Task<Result<CourseDto>> UpdateCourseAsync(Guid courseId, UpdateCourseRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CourseDto>.Failure("Tenant context is not resolved.", 400);

        var course = await _db.TrainingCourses.FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken);
        if (course is null)
            return Result<CourseDto>.Failure("Course not found.", 404);

        if (string.IsNullOrWhiteSpace(request.Title))
            return Result<CourseDto>.Failure("Course title is required.", 400, "validation_error");

        if (!Enum.TryParse<TrainingMode>(request.Mode, true, out var mode))
            return Result<CourseDto>.Failure("Invalid training mode.", 400, "validation_error");

        if (request.Capacity is < 0)
            return Result<CourseDto>.Failure("Capacity must be zero or a positive number.", 400, "validation_error");

        if (request.StartDate is { } sd && request.EndDate is { } ed && ed < sd)
            return Result<CourseDto>.Failure("End date must be on or after the start date.", 400, "validation_error");

        course.Title = request.Title.Trim();
        course.Description = request.Description?.Trim();
        course.Mode = mode;
        course.Capacity = request.Capacity;
        course.Location = request.Location?.Trim();
        course.Instructor = request.Instructor?.Trim();
        course.StartDate = request.StartDate;
        course.EndDate = request.EndDate;
        course.DurationHours = request.DurationHours;

        AddAudit("Training.CourseUpdated", "TrainingCourse", course.Id, $"Course '{course.Title}' updated.");
        await _db.SaveChangesAsync(cancellationToken);

        var counts = await LoadCountsAsync([course.Id], cancellationToken);
        return Result<CourseDto>.Success(ToCourseDto(course, counts));
    }

    public async Task<Result<CourseDto>> ChangeStatusAsync(Guid courseId, string targetStatus, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CourseDto>.Failure("Tenant context is not resolved.", 400);

        if (!Enum.TryParse<CourseStatus>(targetStatus, true, out var target))
            return Result<CourseDto>.Failure("Invalid course status.", 400, "validation_error");

        var course = await _db.TrainingCourses.FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken);
        if (course is null)
            return Result<CourseDto>.Failure("Course not found.", 404);

        if (!IsLegalTransition(course.Status, target))
            return Result<CourseDto>.Failure(
                $"Cannot change course status from {course.Status} to {target}.", 409, "invalid_status_transition");

        var from = course.Status;
        course.Status = target;
        AddAudit("Training.CourseStatusChanged", "TrainingCourse", course.Id, $"Course status {from} → {target}.");
        await _db.SaveChangesAsync(cancellationToken);

        var counts = await LoadCountsAsync([course.Id], cancellationToken);
        return Result<CourseDto>.Success(ToCourseDto(course, counts));
    }

    /// <summary>
    /// Legal course status transitions (AC-2): Draft → Open/Cancelled; Open → Closed/Cancelled/Completed;
    /// Closed → Completed/Cancelled; Cancelled/Completed are terminal.
    /// </summary>
    internal static bool IsLegalTransition(CourseStatus from, CourseStatus to) => from switch
    {
        CourseStatus.Draft => to is CourseStatus.Open or CourseStatus.Cancelled,
        CourseStatus.Open => to is CourseStatus.Closed or CourseStatus.Cancelled or CourseStatus.Completed,
        CourseStatus.Closed => to is CourseStatus.Completed or CourseStatus.Cancelled,
        _ => false,
    };

    // ── Enroll (AC-4/AC-5/AC-6, NFR-3) ──────────────────────────────────────────

    public async Task<Result<EnrollmentDto>> EnrollAsync(Guid courseId, EnrollRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EnrollmentDto>.Failure("Tenant context is not resolved.", 400);

        var selfEmployeeId = await ResolveCurrentEmployeeIdAsync(cancellationToken);

        // Resolve the target employee: null = self-enroll; a different employee requires Manage (AC-4/BR-4).
        Guid targetEmployeeId;
        if (request.EmployeeId is { } requested)
        {
            if (requested != selfEmployeeId && !HasManage)
                return Result<EnrollmentDto>.Failure("You may only enroll yourself.", 403, "forbidden");
            targetEmployeeId = requested;
        }
        else
        {
            if (selfEmployeeId is not { } self)
                return Result<EnrollmentDto>.Failure("No employee record is linked to your account.", 400, "no_employee");
            targetEmployeeId = self;
        }

        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == targetEmployeeId, cancellationToken);
        if (employee is null)
            return Result<EnrollmentDto>.Failure("Employee not found.", 404, "employee_not_found");

        // GAP-026 / C4: the same defect the register recorded against BenefitEnrollmentService, one service
        // over — status was never consulted, so a terminated employee could be enrolled onto a course. Filed
        // as one gap; it is one CLASS, and closing only the named instance is how it comes back.
        //
        // NEW enrollments only, same as benefits: an existing enrollment is ended explicitly by a human, not
        // silently by a deploy.
        if (!employee.Status.CanStartNewActivity())
            return Result<EnrollmentDto>.Failure(
                "This employee's employment status does not allow new course enrollments.",
                422, "employee_not_enrollable");

        return await RunRaceSafeAsync(
            () => EnrollCoreAsync(courseId, employee, cancellationToken), cancellationToken);
    }

    private async Task<Result<EnrollmentDto>> EnrollCoreAsync(Guid courseId, Employee employee, CancellationToken cancellationToken)
    {
        // The execution strategy may re-invoke this on a transient retry — drop any intents queued by a prior
        // (rolled-back) attempt so only the winning attempt's notification flushes.
        _pending.Clear();

        await LockCourseRowAsync(courseId, cancellationToken);

        var course = await _db.TrainingCourses.FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken);
        if (course is null)
            return Result<EnrollmentDto>.Failure("Course not found.", 404);

        // BR-1/AC-4: enrollment only on Open courses.
        if (course.Status != CourseStatus.Open)
            return Result<EnrollmentDto>.Failure("This course is not open for enrollment.", 409, "course_not_open");

        // BR-2/AC-6: one active enrollment per (course, employee).
        var alreadyActive = await _db.CourseEnrollments.AnyAsync(
            e => e.CourseId == courseId && e.EmployeeId == employee.Id
                 && (e.Status == EnrollmentStatus.Enrolled || e.Status == EnrollmentStatus.Waitlisted),
            cancellationToken);
        if (alreadyActive)
            return Result<EnrollmentDto>.Failure("This employee is already enrolled in the course.", 409, "already_enrolled");

        var enrolledCount = await _db.CourseEnrollments.CountAsync(
            e => e.CourseId == courseId && e.Status == EnrollmentStatus.Enrolled, cancellationToken);

        var enrollment = new CourseEnrollment
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            CourseId = courseId,
            EmployeeId = employee.Id,
            EnrolledAt = DateTime.UtcNow,
            EnrolledBy = _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty,
            IsDeleted = false,
        };

        // BR-5: null capacity = unlimited (never waitlists). AC-5: full course → FIFO waitlist.
        if (course.Capacity is null || enrolledCount < course.Capacity.Value)
        {
            enrollment.Status = EnrollmentStatus.Enrolled;
            enrollment.WaitlistPosition = null;
            QueueNotification("training_enrollment_confirmed", employee, course, EnrollmentStatus.Enrolled);
        }
        else
        {
            var maxPosition = await _db.CourseEnrollments
                .Where(e => e.CourseId == courseId && e.Status == EnrollmentStatus.Waitlisted)
                .MaxAsync(e => (int?)e.WaitlistPosition, cancellationToken) ?? 0;
            enrollment.Status = EnrollmentStatus.Waitlisted;
            enrollment.WaitlistPosition = maxPosition + 1;
            QueueNotification("training_waitlisted", employee, course, EnrollmentStatus.Waitlisted);
        }

        _db.CourseEnrollments.Add(enrollment);
        AddAudit("Training.Enrolled", "CourseEnrollment", enrollment.Id,
            $"{employee.FirstName} {employee.LastName} {enrollment.Status} in '{course.Title}'.");
        await _db.SaveChangesAsync(cancellationToken);

        return Result<EnrollmentDto>.Success(ToEnrollmentDto(enrollment, course.Title, employee));
    }

    // ── Cancel (AC-7, BR-3) ─────────────────────────────────────────────────────

    public async Task<Result<EnrollmentDto>> CancelAsync(Guid enrollmentId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EnrollmentDto>.Failure("Tenant context is not resolved.", 400);

        var enrollment = await _db.CourseEnrollments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == enrollmentId, cancellationToken);
        if (enrollment is null)
            return Result<EnrollmentDto>.Failure("Enrollment not found.", 404);

        // BR-4: own enrollment (self) or Manage.
        var selfEmployeeId = await ResolveCurrentEmployeeIdAsync(cancellationToken);
        if (enrollment.EmployeeId != selfEmployeeId && !HasManage)
            return Result<EnrollmentDto>.Failure("You may only cancel your own enrollment.", 403, "forbidden");

        if (enrollment.Status is not (EnrollmentStatus.Enrolled or EnrollmentStatus.Waitlisted))
            return Result<EnrollmentDto>.Failure("This enrollment cannot be cancelled.", 409, "not_cancellable");

        return await RunRaceSafeAsync(
            () => CancelCoreAsync(enrollmentId, cancellationToken), cancellationToken);
    }

    private async Task<Result<EnrollmentDto>> CancelCoreAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        _pending.Clear();

        var enrollment = await _db.CourseEnrollments.FirstOrDefaultAsync(e => e.Id == enrollmentId, cancellationToken);
        if (enrollment is null)
            return Result<EnrollmentDto>.Failure("Enrollment not found.", 404);

        await LockCourseRowAsync(enrollment.CourseId, cancellationToken);
        // Re-read under the lock in case a concurrent cancel already actioned this row.
        await _db.Entry(enrollment).ReloadAsync(cancellationToken);

        if (enrollment.Status is not (EnrollmentStatus.Enrolled or EnrollmentStatus.Waitlisted))
            return Result<EnrollmentDto>.Failure("This enrollment cannot be cancelled.", 409, "not_cancellable");

        var course = await _db.TrainingCourses.FirstOrDefaultAsync(c => c.Id == enrollment.CourseId, cancellationToken);
        var courseTitle = course?.Title ?? string.Empty;

        var wasEnrolled = enrollment.Status == EnrollmentStatus.Enrolled;
        enrollment.Status = EnrollmentStatus.Cancelled;
        enrollment.CancelledAt = DateTime.UtcNow;
        enrollment.WaitlistPosition = null;

        var cancelledEmployee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == enrollment.EmployeeId, cancellationToken);
        if (cancelledEmployee is not null && course is not null)
            QueueNotification("training_enrollment_cancelled", cancelledEmployee, course, EnrollmentStatus.Cancelled);

        // BR-3/AC-7: a freed Enrolled seat promotes the FIFO waitlist head.
        if (wasEnrolled)
        {
            var head = await _db.CourseEnrollments
                .Where(e => e.CourseId == enrollment.CourseId && e.Status == EnrollmentStatus.Waitlisted)
                .OrderBy(e => e.WaitlistPosition)
                .FirstOrDefaultAsync(cancellationToken);

            if (head is not null)
            {
                head.Status = EnrollmentStatus.Enrolled;
                head.WaitlistPosition = null;

                var promotedEmployee = await _db.Employees.AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == head.EmployeeId, cancellationToken);
                if (promotedEmployee is not null && course is not null)
                    QueueNotification("training_waitlist_promoted", promotedEmployee, course, EnrollmentStatus.Enrolled);

                AddAudit("Training.WaitlistPromoted", "CourseEnrollment", head.Id,
                    $"Enrollment {head.Id} promoted from the waitlist in '{courseTitle}'.");
            }
        }

        AddAudit("Training.Cancelled", "CourseEnrollment", enrollment.Id,
            $"Enrollment {enrollment.Id} cancelled in '{courseTitle}'.");
        await _db.SaveChangesAsync(cancellationToken);

        return Result<EnrollmentDto>.Success(ToEnrollmentDto(enrollment, courseTitle, cancelledEmployee));
    }

    // ── Complete (AC-8, Manage-gated) ───────────────────────────────────────────

    public async Task<Result<EnrollmentDto>> CompleteAsync(Guid enrollmentId, CompleteEnrollmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EnrollmentDto>.Failure("Tenant context is not resolved.", 400);

        if (request.Score is < 0 or > 100)
            return Result<EnrollmentDto>.Failure("Score must be between 0 and 100.", 400, "validation_error");

        var enrollment = await _db.CourseEnrollments.FirstOrDefaultAsync(e => e.Id == enrollmentId, cancellationToken);
        if (enrollment is null)
            return Result<EnrollmentDto>.Failure("Enrollment not found.", 404);

        if (enrollment.Status != EnrollmentStatus.Enrolled)
            return Result<EnrollmentDto>.Failure(
                "Only an active (Enrolled) enrollment can be marked completed.", 409, "not_completable");

        enrollment.Status = EnrollmentStatus.Completed;
        enrollment.CompletedAt = DateTime.UtcNow;
        enrollment.CertificateReference = request.CertificateReference?.Trim();
        enrollment.Score = request.Score;

        var course = await _db.TrainingCourses.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == enrollment.CourseId, cancellationToken);
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == enrollment.EmployeeId, cancellationToken);

        if (employee is not null && course is not null)
            QueueNotification("training_completed", employee, course, EnrollmentStatus.Completed);

        AddAudit("Training.Completed", "CourseEnrollment", enrollment.Id,
            $"Enrollment {enrollment.Id} marked completed in '{course?.Title}'.");
        await _db.SaveChangesAsync(cancellationToken);

        await FlushNotificationsAsync(cancellationToken);

        return Result<EnrollmentDto>.Success(ToEnrollmentDto(enrollment, course?.Title ?? string.Empty, employee));
    }

    // ── Enrollment reads (AC-9) ─────────────────────────────────────────────────

    public async Task<Result<IReadOnlyList<EnrollmentDto>>> GetMyEnrollmentsAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<EnrollmentDto>>.Failure("Tenant context is not resolved.", 400);

        var selfEmployeeId = await ResolveCurrentEmployeeIdAsync(cancellationToken);
        if (selfEmployeeId is not { } empId)
            return Result<IReadOnlyList<EnrollmentDto>>.Success([]);

        return await ReadEnrollmentsForEmployeeAsync(empId, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<EnrollmentDto>>> GetTrainingHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<EnrollmentDto>>.Failure("Tenant context is not resolved.", 400);

        // AC-9/FR-7: self is allowed with View.Own; another employee requires View.All or Manage.
        var selfEmployeeId = await ResolveCurrentEmployeeIdAsync(cancellationToken);
        if (employeeId != selfEmployeeId && !HasViewAll)
            return Result<IReadOnlyList<EnrollmentDto>>.Failure(
                "You may only view your own training history.", 403, "forbidden");

        return await ReadEnrollmentsForEmployeeAsync(employeeId, cancellationToken);
    }

    private async Task<Result<IReadOnlyList<EnrollmentDto>>> ReadEnrollmentsForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var enrollments = await _db.CourseEnrollments.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .OrderByDescending(e => e.EnrolledAt)
            .ToListAsync(cancellationToken);

        if (enrollments.Count == 0)
            return Result<IReadOnlyList<EnrollmentDto>>.Success([]);

        // Resolve course titles + employee name via separate lookups (avoids projecting through a filtered
        // navigation, which returns empty on the InMemory provider).
        var courseIds = enrollments.Select(e => e.CourseId).Distinct().ToList();
        var titles = await _db.TrainingCourses.AsNoTracking()
            .Where(c => courseIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Title, cancellationToken);

        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        var employeeName = employee is null ? string.Empty : $"{employee.FirstName} {employee.LastName}".Trim();

        var dtos = enrollments
            .Select(e => new EnrollmentDto
            {
                Id = e.Id,
                CourseId = e.CourseId,
                CourseTitle = titles.GetValueOrDefault(e.CourseId, string.Empty),
                EmployeeId = e.EmployeeId,
                EmployeeName = employeeName,
                Status = e.Status.ToString(),
                WaitlistPosition = e.WaitlistPosition,
                EnrolledAt = e.EnrolledAt,
                CompletedAt = e.CompletedAt,
                CertificateReference = e.CertificateReference,
                Score = e.Score,
            })
            .ToList();

        return Result<IReadOnlyList<EnrollmentDto>>.Success(dtos);
    }

    // ── Race-safe execution helpers (NFR-3, BUG-068 class) ──────────────────────

    /// <summary>
    /// Runs <paramref name="core"/> inside the Npgsql retry-safe execution strategy wrapping a transaction on a
    /// relational provider (so the FOR UPDATE row lock serializes concurrent writers), or directly on InMemory
    /// (no transactions/raw-SQL). Notifications flush only after a successful commit.
    /// </summary>
    private async Task<Result<EnrollmentDto>> RunRaceSafeAsync(
        Func<Task<Result<EnrollmentDto>>> core, CancellationToken cancellationToken)
    {
        Result<EnrollmentDto> result;
        if (!_db.Database.IsRelational())
        {
            result = await core();
        }
        else
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            result = await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
                var r = await core();
                if (r.IsSuccess)
                    await tx.CommitAsync(cancellationToken);
                else
                    await tx.RollbackAsync(cancellationToken);
                return r;
            });
        }

        if (result.IsSuccess)
            await FlushNotificationsAsync(cancellationToken);
        else
            _pending.Clear();

        return result;
    }

    /// <summary>Pessimistic row lock on the course to serialize concurrent seat-claims (no-op on InMemory).</summary>
    private async Task LockCourseRowAsync(Guid courseId, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
            return;
        // nosemgrep: hrm-raw-sql-no-tenant-predicate -- `SELECT 1 ... FOR UPDATE` by primary key: a row LOCK returning no columns. The id came from a tenant-filtered EF query, and RLS caps the lock to the current tenant regardless.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM training_courses WHERE id = {courseId} FOR UPDATE", cancellationToken);
    }

    private async Task<Guid?> ResolveCurrentEmployeeIdAsync(CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == Guid.Empty)
            return null;
        return await _db.Employees.AsNoTracking()
            .Where(e => e.UserId == _currentUser.UserId)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // ── Counts + mapping ────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<(Guid, EnrollmentStatus), int> EmptyCounts =
        new Dictionary<(Guid, EnrollmentStatus), int>();

    private async Task<IReadOnlyDictionary<(Guid, EnrollmentStatus), int>> LoadCountsAsync(
        IReadOnlyList<Guid> courseIds, CancellationToken cancellationToken)
    {
        if (courseIds.Count == 0)
            return EmptyCounts;

        var rows = await _db.CourseEnrollments.AsNoTracking()
            .Where(e => courseIds.Contains(e.CourseId)
                        && (e.Status == EnrollmentStatus.Enrolled || e.Status == EnrollmentStatus.Waitlisted))
            .GroupBy(e => new { e.CourseId, e.Status })
            .Select(g => new { g.Key.CourseId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => (r.CourseId, r.Status), r => r.Count);
    }

    private static CourseDto ToCourseDto(TrainingCourse c, IReadOnlyDictionary<(Guid, EnrollmentStatus), int> counts) => new()
    {
        Id = c.Id,
        Title = c.Title,
        Description = c.Description,
        Mode = c.Mode.ToString(),
        Capacity = c.Capacity,
        Location = c.Location,
        Instructor = c.Instructor,
        StartDate = c.StartDate,
        EndDate = c.EndDate,
        DurationHours = c.DurationHours,
        Status = c.Status.ToString(),
        EnrolledCount = counts.GetValueOrDefault((c.Id, EnrollmentStatus.Enrolled), 0),
        WaitlistCount = counts.GetValueOrDefault((c.Id, EnrollmentStatus.Waitlisted), 0),
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };

    private static EnrollmentDto ToEnrollmentDto(CourseEnrollment e, string courseTitle, Employee? employee) => new()
    {
        Id = e.Id,
        CourseId = e.CourseId,
        CourseTitle = courseTitle,
        EmployeeId = e.EmployeeId,
        EmployeeName = employee is null ? string.Empty : $"{employee.FirstName} {employee.LastName}".Trim(),
        Status = e.Status.ToString(),
        WaitlistPosition = e.WaitlistPosition,
        EnrolledAt = e.EnrolledAt,
        CompletedAt = e.CompletedAt,
        CertificateReference = e.CertificateReference,
        Score = e.Score,
    };

    // ── Audit (FR-10) ───────────────────────────────────────────────────────────

    private void AddAudit(string action, string resourceType, Guid resourceId, string detail)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId.ToString(),
            Detail = detail,
            CreatedAt = DateTime.UtcNow,
        });
    }

    // ── Notifications (US-NTF-006, FR-9) ────────────────────────────────────────

    private void QueueNotification(string eventKey, Employee employee, TrainingCourse course, EnrollmentStatus status)
    {
        // Skip employees with no linked user (nothing to notify — email address is unknown too).
        if (employee.UserId is not { } userId || userId == Guid.Empty)
            return;
        _pending.Add(new PendingNotification(
            userId, eventKey, course.Title, employee.FirstName, employee.LastName, status.ToString()));
    }

    /// <summary>
    /// Dispatch the queued notification intents AFTER the state is committed. Each leg is wrapped in a try/catch
    /// that logs and swallows — a notification failure must never fail the enrollment action. No-ops when no
    /// dispatcher is wired (the direct-construction test harnesses).
    /// </summary>
    private async Task FlushNotificationsAsync(CancellationToken cancellationToken)
    {
        if (_dispatcher is null || _pending.Count == 0)
        {
            _pending.Clear();
            return;
        }

        var intents = _pending.ToList();
        _pending.Clear();

        foreach (var intent in intents)
        {
            try
            {
                var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["course"] = new Dictionary<string, object?> { ["title"] = intent.CourseTitle },
                    ["employee"] = new Dictionary<string, object?>
                    {
                        ["firstName"] = intent.EmployeeFirst,
                        ["lastName"] = intent.EmployeeLast,
                    },
                    ["enrollment"] = new Dictionary<string, object?> { ["status"] = intent.Status },
                });
                var request = new NotificationRequest(
                    TenantId: _tenantContext.TenantId,
                    EventKey: intent.EventKey,
                    PayloadJson: payloadJson,
                    RecipientUserId: intent.UserId,
                    RecipientEmail: null,
                    NotificationType: null);

                await _dispatcher.SendInAppAsync(request, cancellationToken);
                await _dispatcher.SendEmailAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Training notification {EventKey} for user {UserId} failed (non-fatal).",
                    intent.EventKey, intent.UserId);
            }
        }
    }
}
