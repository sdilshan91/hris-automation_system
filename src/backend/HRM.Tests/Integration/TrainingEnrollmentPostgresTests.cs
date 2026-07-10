// ============================================================================
// US-TRN-001 — capacity/waitlist, FIFO promotion, race-safe concurrency, duplicate-block, status
// transitions, catalog visibility, completion + history, and tenant isolation for TrainingService.
//
// HARNESS = real Postgres via Testcontainers (NOT InMemory). Load-bearing: the InMemory provider has no
// transactions and no `SELECT … FOR UPDATE`, so the single-winner / no-oversubscribe guarantee (NFR-3,
// BUG-068 class) can only be exercised on Postgres. Schema is built with EnsureCreatedAsync() (mirrors the
// other *PostgresTests — MigrateAsync is avoided because an unrelated uncommitted model change would trip
// PendingModelChangesWarning). RLS stays dormant (flag off) — isolation here is the EF global query filter.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Training.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-TRN-001")]
public sealed class TrainingEnrollmentPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();
    private readonly Guid _userC = Guid.NewGuid();
    private readonly Guid _empA = BaseEntity.NewUuidV7();
    private readonly Guid _empB = BaseEntity.NewUuidV7();
    private readonly Guid _empC = BaseEntity.NewUuidV7();

    // Tenant B: a separate tenant used for the isolation test.
    private readonly Guid _userD = Guid.NewGuid();
    private readonly Guid _empD = BaseEntity.NewUuidV7();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var tc = Tc(_tenantA);
        await using var db = Db(tc, User(_userA, _tenantA, PermissionCatalog.Training.Manage));
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme" });
        db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex" });

        db.Users.AddRange(
            new User { Id = _userA, Email = "a@t.com" },
            new User { Id = _userB, Email = "b@t.com" },
            new User { Id = _userC, Email = "c@t.com" },
            new User { Id = _userD, Email = "d@t.com" });

        var deptA = BaseEntity.NewUuidV7();
        var deptB = BaseEntity.NewUuidV7();
        var jobA = BaseEntity.NewUuidV7();
        var jobB = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = deptA, TenantId = _tenantA, Name = "Eng", Code = "ENG" });
        db.Departments.Add(new Department { Id = deptB, TenantId = _tenantB, Name = "Eng", Code = "ENG" });
        db.JobTitles.Add(new JobTitle { Id = jobA, TenantId = _tenantA, TitleName = "Engineer" });
        db.JobTitles.Add(new JobTitle { Id = jobB, TenantId = _tenantB, TitleName = "Engineer" });

        db.Employees.AddRange(
            new Employee { Id = _empA, TenantId = _tenantA, UserId = _userA, EmployeeNo = "A", FirstName = "Ann", LastName = "A", Email = "a@t.com", Status = EmployeeStatus.Active, DepartmentId = deptA, JobTitleId = jobA },
            new Employee { Id = _empB, TenantId = _tenantA, UserId = _userB, EmployeeNo = "B", FirstName = "Ben", LastName = "B", Email = "b@t.com", Status = EmployeeStatus.Active, DepartmentId = deptA, JobTitleId = jobA },
            new Employee { Id = _empC, TenantId = _tenantA, UserId = _userC, EmployeeNo = "C", FirstName = "Cal", LastName = "C", Email = "c@t.com", Status = EmployeeStatus.Active, DepartmentId = deptA, JobTitleId = jobA });
        db.Employees.Add(
            new Employee { Id = _empD, TenantId = _tenantB, UserId = _userD, EmployeeNo = "D", FirstName = "Dan", LastName = "D", Email = "d@t.com", Status = EmployeeStatus.Active, DepartmentId = deptB, JobTitleId = jobB });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── AC-4/AC-5/AC-7: capacity, FIFO waitlist, and cancel-promotes ────────────

    [Fact]
    public async Task Capacity1_SecondEnrollWaitlisted_And_CancelPromotesFifo()
    {
        var courseId = await SeedCourseAsync(_tenantA, CourseStatus.Open, capacity: 1);

        // A enrolls → the only seat.
        var a = await Enroll(_tenantA, _userA, courseId);
        a.IsSuccess.Should().BeTrue();
        a.Value!.Status.Should().Be("Enrolled");

        // B enrolls → waitlisted position 1.
        var b = await Enroll(_tenantA, _userB, courseId);
        b.IsSuccess.Should().BeTrue();
        b.Value!.Status.Should().Be("Waitlisted");
        b.Value.WaitlistPosition.Should().Be(1);

        // C enrolls → waitlisted position 2 (FIFO).
        var c = await Enroll(_tenantA, _userC, courseId);
        c.Value!.Status.Should().Be("Waitlisted");
        c.Value.WaitlistPosition.Should().Be(2);

        // A cancels → the head of the waitlist (B) is promoted to Enrolled; C stays waitlisted.
        var cancel = await Cancel(_tenantA, _userA, a.Value.Id);
        cancel.IsSuccess.Should().BeTrue();

        await using var verify = Db(Tc(_tenantA), User(_userA, _tenantA, PermissionCatalog.Training.Manage));
        var enrollments = await verify.CourseEnrollments.AsNoTracking()
            .Where(e => e.CourseId == courseId).ToListAsync();

        enrollments.Single(e => e.EmployeeId == _empA).Status.Should().Be(EnrollmentStatus.Cancelled);
        var promoted = enrollments.Single(e => e.EmployeeId == _empB);
        promoted.Status.Should().Be(EnrollmentStatus.Enrolled);
        promoted.WaitlistPosition.Should().BeNull();
        enrollments.Single(e => e.EmployeeId == _empC).Status.Should().Be(EnrollmentStatus.Waitlisted);
    }

    // ── FR-10: every enrollment mutation writes an audit_log row (not silently dropped) ─────

    [Fact]
    public async Task EnrollCancelPromote_WritesAuditRows()
    {
        var courseId = await SeedCourseAsync(_tenantA, CourseStatus.Open, capacity: 1);

        var a = await Enroll(_tenantA, _userA, courseId); // seat
        await Enroll(_tenantA, _userB, courseId);         // waitlisted
        await Cancel(_tenantA, _userA, a.Value!.Id);      // → promotes B

        await using var verify = Db(Tc(_tenantA), User(_userA, _tenantA, PermissionCatalog.Training.Manage));
        var events = await verify.AuditLogs.AsNoTracking()
            .Where(l => l.EventType.StartsWith("Training."))
            .Select(l => l.EventType)
            .ToListAsync();

        // A dropped AddAudit(...) on any of these mutation paths would otherwise pass the suite green (FR-10).
        events.Should().Contain("Training.Enrolled");
        events.Should().Contain("Training.Cancelled");
        events.Should().Contain("Training.WaitlistPromoted");
    }

    // ── NFR-3 (BUG-068 class): concurrent enrolls on the last seat must not oversubscribe ───

    [Fact]
    public async Task ConcurrentEnrolls_Capacity1_ExactlyOneEnrolled_OneWaitlisted()
    {
        var courseId = await SeedCourseAsync(_tenantA, CourseStatus.Open, capacity: 1);

        async Task<Result<EnrollmentDto>> EnrollOnceAsync(Guid userId)
        {
            await using var db = Db(Tc(_tenantA), User(userId, _tenantA, PermissionCatalog.Training.ViewOwn));
            var service = Service(db, User(userId, _tenantA, PermissionCatalog.Training.ViewOwn));
            return await service.EnrollAsync(courseId, new EnrollRequest(), CancellationToken.None);
        }

        var t1 = Task.Run(() => EnrollOnceAsync(_userA));
        var t2 = Task.Run(() => EnrollOnceAsync(_userB));
        var results = await Task.WhenAll(t1, t2);

        results.Should().OnlyContain(r => r.IsSuccess);
        results.Count(r => r.Value!.Status == "Enrolled").Should().Be(1, "the single seat may only go to one enroller");
        results.Count(r => r.Value!.Status == "Waitlisted").Should().Be(1, "the other must be waitlisted, not a second seat");

        await using var verify = Db(Tc(_tenantA), User(_userA, _tenantA, PermissionCatalog.Training.Manage));
        var enrolled = await verify.CourseEnrollments.AsNoTracking()
            .CountAsync(e => e.CourseId == courseId && e.Status == EnrollmentStatus.Enrolled);
        enrolled.Should().Be(1, "capacity must never be oversubscribed under concurrency");
    }

    // ── AC-6: duplicate active enrollment → 409 already_enrolled ────────────────

    [Fact]
    public async Task DuplicateEnroll_Returns409AlreadyEnrolled()
    {
        var courseId = await SeedCourseAsync(_tenantA, CourseStatus.Open, capacity: null);

        (await Enroll(_tenantA, _userA, courseId)).IsSuccess.Should().BeTrue();

        var dup = await Enroll(_tenantA, _userA, courseId);
        dup.IsFailure.Should().BeTrue();
        dup.StatusCode.Should().Be(409);
        dup.ErrorCode.Should().Be("already_enrolled");
    }

    // ── AC-2/AC-4: Draft not enrollable; illegal status transition → 409 ────────

    [Fact]
    public async Task DraftCourse_NotEnrollable_409CourseNotOpen()
    {
        var courseId = await SeedCourseAsync(_tenantA, CourseStatus.Draft, capacity: null);

        var enroll = await Enroll(_tenantA, _userA, courseId);
        enroll.IsFailure.Should().BeTrue();
        enroll.StatusCode.Should().Be(409);
        enroll.ErrorCode.Should().Be("course_not_open");
    }

    [Fact]
    public async Task StatusTransitions_LegalOpens_IllegalRejected409()
    {
        var courseId = await SeedCourseAsync(_tenantA, CourseStatus.Draft, capacity: null);

        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA, PermissionCatalog.Training.Manage));
        var service = Service(db, User(_userA, _tenantA, PermissionCatalog.Training.Manage));

        // Draft → Open is legal and makes the course enrollable.
        var open = await service.ChangeStatusAsync(courseId, "Open", CancellationToken.None);
        open.IsSuccess.Should().BeTrue();
        open.Value!.Status.Should().Be("Open");

        // Open → Draft is NOT a legal transition.
        var illegal = await service.ChangeStatusAsync(courseId, "Draft", CancellationToken.None);
        illegal.IsFailure.Should().BeTrue();
        illegal.StatusCode.Should().Be(409);
        illegal.ErrorCode.Should().Be("invalid_status_transition");
    }

    // ── AC-3: catalog visibility — non-ViewAll sees only Open ───────────────────

    [Fact]
    public async Task Visibility_NonViewAllCaller_SeesOnlyOpenCourses()
    {
        await SeedCourseAsync(_tenantA, CourseStatus.Open, capacity: null);
        await SeedCourseAsync(_tenantA, CourseStatus.Draft, capacity: null);
        await SeedCourseAsync(_tenantA, CourseStatus.Closed, capacity: null);

        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA, PermissionCatalog.Training.ViewOwn));
        var ownService = Service(db, User(_userA, _tenantA, PermissionCatalog.Training.ViewOwn));
        var ownList = await ownService.GetCoursesAsync(CancellationToken.None);
        ownList.Value!.Should().OnlyContain(c => c.Status == "Open");
        ownList.Value.Count.Should().Be(1);

        var allService = Service(db, User(_userA, _tenantA, PermissionCatalog.Training.ViewAll));
        var allList = await allService.GetCoursesAsync(CancellationToken.None);
        allList.Value!.Count.Should().Be(3, "a View.All caller sees all statuses");
    }

    // ── AC-8/AC-9: completion appears on the employee's training history ────────

    [Fact]
    public async Task Complete_MarksCompleted_ShowsInTrainingHistory()
    {
        var courseId = await SeedCourseAsync(_tenantA, CourseStatus.Open, capacity: null);
        var enroll = await Enroll(_tenantA, _userA, courseId);

        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA, PermissionCatalog.Training.Manage));
        var service = Service(db, User(_userA, _tenantA, PermissionCatalog.Training.Manage));

        var complete = await service.CompleteAsync(
            enroll.Value!.Id, new CompleteEnrollmentRequest { CertificateReference = "CERT-1", Score = 88m },
            CancellationToken.None);
        complete.IsSuccess.Should().BeTrue();
        complete.Value!.Status.Should().Be("Completed");
        complete.Value.CertificateReference.Should().Be("CERT-1");
        complete.Value.Score.Should().Be(88m);

        var history = await service.GetTrainingHistoryAsync(_empA, CancellationToken.None);
        history.Value!.Should().ContainSingle();
        history.Value[0].Status.Should().Be("Completed");
        history.Value[0].CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TrainingHistory_ViewOwnCaller_CannotReadAnotherEmployee_403()
    {
        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA, PermissionCatalog.Training.ViewOwn));
        var service = Service(db, User(_userA, _tenantA, PermissionCatalog.Training.ViewOwn));

        var others = await service.GetTrainingHistoryAsync(_empB, CancellationToken.None);
        others.IsFailure.Should().BeTrue();
        others.StatusCode.Should().Be(403);
    }

    // ── AC-10: tenant isolation — Tenant A cannot see Tenant B's course/enrollment ──

    [Fact]
    public async Task TenantIsolation_CoursesAndEnrollmentsDoNotLeak()
    {
        // Tenant B: a course + an enrollment.
        var courseB = await SeedCourseAsync(_tenantB, CourseStatus.Open, capacity: null);
        var enrollB = await Enroll(_tenantB, _userD, courseB);
        enrollB.IsSuccess.Should().BeTrue();

        // Tenant A: a course of its own.
        var courseA = await SeedCourseAsync(_tenantA, CourseStatus.Open, capacity: null);

        // Tenant A (View.All) lists → only its own course, never Tenant B's.
        await using var db = Db(Tc(_tenantA), User(_userA, _tenantA, PermissionCatalog.Training.ViewAll));
        var service = Service(db, User(_userA, _tenantA, PermissionCatalog.Training.ViewAll));
        var list = await service.GetCoursesAsync(CancellationToken.None);
        list.Value!.Select(c => c.Id).Should().Contain(courseA).And.NotContain(courseB);

        // Tenant A cannot fetch Tenant B's course by id (query filter → 404).
        var cross = await service.GetCourseByIdAsync(courseB, CancellationToken.None);
        cross.IsFailure.Should().BeTrue();
        cross.StatusCode.Should().Be(404);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedCourseAsync(Guid tenantId, CourseStatus status, int? capacity)
    {
        var id = BaseEntity.NewUuidV7();
        await using var db = Db(Tc(tenantId), User(Guid.NewGuid(), tenantId, PermissionCatalog.Training.Manage));
        db.TrainingCourses.Add(new TrainingCourse
        {
            Id = id, TenantId = tenantId, Title = "Course " + status, Mode = TrainingMode.Online,
            Capacity = capacity, Status = status,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Result<EnrollmentDto>> Enroll(Guid tenantId, Guid userId, Guid courseId)
    {
        await using var db = Db(Tc(tenantId), User(userId, tenantId, PermissionCatalog.Training.ViewOwn));
        var service = Service(db, User(userId, tenantId, PermissionCatalog.Training.ViewOwn));
        return await service.EnrollAsync(courseId, new EnrollRequest(), CancellationToken.None);
    }

    private async Task<Result<EnrollmentDto>> Cancel(Guid tenantId, Guid userId, Guid enrollmentId)
    {
        await using var db = Db(Tc(tenantId), User(userId, tenantId, PermissionCatalog.Training.Manage));
        var service = Service(db, User(userId, tenantId, PermissionCatalog.Training.Manage));
        return await service.CancelAsync(enrollmentId, CancellationToken.None);
    }

    private AppDbContext Db(ITenantContext tc, ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .EnableDetailedErrors()
            .UseNpgsql(_postgres.GetConnectionString() + ";Include Error Detail=true", n =>
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(user))
            .Options, tc);

    private static TrainingService Service(AppDbContext db, ICurrentUser user) =>
        new(db, MutableTenantContext.For(user.TenantId), user, NullLogger<TrainingService>.Instance);

    private static ITenantContext Tc(Guid tenantId) => MutableTenantContext.For(tenantId);

    private static ICurrentUser User(Guid userId, Guid tenantId, params string[] permissions)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.TenantId.Returns(tenantId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("user@acme.com");
        u.Permissions.Returns(permissions);
        return u;
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
        public static MutableTenantContext For(Guid tenantId) => new() { TenantId = tenantId };
    }
}
