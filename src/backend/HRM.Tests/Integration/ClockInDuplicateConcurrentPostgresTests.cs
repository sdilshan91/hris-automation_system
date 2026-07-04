// ============================================================================
// BUG-047 (HIGH, Attendance, US-ATT-002/US-ATT-001) regression.
//
// AttendanceService.ClockInAsync guards "at most one open record" with an app-level
// pre-check (BR-1: `AnyAsync(a => a.EmployeeId == id && a.ClockOut == null)`). That guard
// has a TOCTOU window: two concurrent clock-ins (or a fast double-submit) can BOTH read
// "no open record", BOTH insert, and the loser then violates the partial unique index
// `ix_attendance_log_open_unique` (tenant_id, employee_id WHERE clock_out IS NULL AND
// is_deleted = false) → Postgres 23505 → DbUpdateException → HTTP 500.
//
// The fix wraps the clock-in SaveChanges in a catch that recognises the 23505 on that
// index and returns the SAME clean 409 `already_clocked_in` the pre-check would have,
// instead of letting the exception bubble as a 500.
//
// WHY POSTGRES (not InMemory): this 500 is DB-index-driven. The EF Core InMemory provider
// does NOT enforce partial unique indexes AND cannot reproduce the concurrent-insert race,
// so pre-fix the second insert would simply succeed (a 2nd open row, 201) and the 500 could
// never reproduce — the test would be theater. This uses the project's Testcontainers/Postgres
// harness (mirrors ShiftNameTrimDuplicatePostgresTests) so the real partial unique index fires.
//
// HOW THE RACE IS MADE DETERMINISTIC (no Thread.Sleep guesswork): a "blocker" context inserts
// an open attendance row inside an UNCOMMITTED transaction. Under READ COMMITTED, the service's
// BR-1 pre-check (running on a separate connection) does NOT see that uncommitted row, so it
// passes the guard and proceeds to its own INSERT — which then BLOCKS on the unique key held by
// the blocker's pending insert. We poll pg_stat_activity until that INSERT is confirmed waiting
// on a Lock, THEN commit the blocker. The blocker's row becomes the winning open record, so the
// service's blocked INSERT wakes and deterministically trips 23505 — exactly the production race.
//   Pre-fix : ClockInAsync lets DbUpdateException bubble → 500 (test observes the throw).
//   Post-fix: ClockInAsync catches the 23505 → clean 409 already_clocked_in, no 2nd open row.
// ============================================================================

using System.Diagnostics;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
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

public sealed class ClockInDuplicateConcurrentPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private Guid _employeeId;

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
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
    }

    // NOTE: no EnableRetryOnFailure here — a manual (blocker) transaction is illegal under the
    // retrying execution strategy, and a 23505 is non-transient anyway (retry would not change it).
    private AppDbContext CreateContext(ITenantContext tc, ICurrentUser cu) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);

    private static AttendanceService BuildService(AppDbContext db, ITenantContext tc, ICurrentUser cu)
    {
        var overtime = new OvertimeService(db, tc, cu, NullLogger<OvertimeService>.Instance);
        var shift = new ShiftService(db, tc, cu, NullLogger<ShiftService>.Instance);
        return new AttendanceService(db, tc, cu, overtime, shift, NullLogger<AttendanceService>.Instance);
    }

    private (ITenantContext tc, ICurrentUser cu) Principals()
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(_userId);
        cu.Email.Returns("worker@acme.test");
        return (tc, cu);
    }

    /// <summary>
    /// Seeds the FK chain a real clock-in needs on Postgres: a User (Employee.UserId FK), a
    /// Department + JobTitle (both required FKs), and an ACTIVE Employee linked to the acting user.
    /// </summary>
    private async Task SeedEmployeeAsync(AppDbContext db)
    {
        db.Set<User>().Add(new User { Id = _userId, Email = "worker@acme.test", IsActive = true });

        var deptId = BaseEntity.NewUuidV7();
        var jobId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = deptId, TenantId = _tenantId, Name = "Ops", Code = "OPS", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = jobId, TenantId = _tenantId, TitleName = "Agent", IsActive = true });

        _employeeId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = _employeeId,
            TenantId = _tenantId,
            UserId = _userId,
            EmployeeNo = "EMP-1",
            FirstName = "Wendy",
            LastName = "Worker",
            Email = "worker@acme.test",
            DateOfJoining = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DepartmentId = deptId,
            JobTitleId = jobId,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Confirms the partial unique index that turns this race into a 500 (rather than a silent 2nd
    /// open row) actually exists after MigrateAsync — otherwise the pre-fix reproduction is vacuous.
    /// </summary>
    private static async Task AssertOpenUniqueIndexExistsAsync(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ix_attendance_log_open_unique';";
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            count.Should().Be(1, "the partial unique index (tenant_id, employee_id WHERE clock_out IS NULL) "
                + "must exist for the pre-fix concurrent-clock-in 500 to be reproducible");
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    /// <summary>
    /// Polls pg_stat_activity (on an independent connection) until at least one backend is actively
    /// WAITING on a Lock while executing a statement that touches attendance_log — i.e. the service's
    /// clock-in INSERT has passed the BR-1 pre-check and is now blocked behind the blocker's key.
    /// This replaces a fragile fixed sleep with a deterministic readiness signal.
    /// </summary>
    private static async Task WaitUntilClockInInsertBlockedAsync(AppDbContext probe, TimeSpan timeout)
    {
        var conn = probe.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM pg_stat_activity
                    WHERE state = 'active'
                      AND wait_event_type = 'Lock'
                      AND query ILIKE '%attendance_log%';";
                var blocked = Convert.ToInt64(await cmd.ExecuteScalarAsync());
                if (blocked >= 1)
                    return;
                await Task.Delay(100);
            }

            throw new TimeoutException(
                "The clock-in INSERT never blocked on the open-record unique index — the concurrency "
                + "race was not set up, so this test cannot distinguish the pre-fix 500 from the fix.");
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    // ── BUG-047: concurrent duplicate clock-in must be a clean 409, not a 500 ──
    [Fact]
    public async Task ClockIn_DuplicateSameDay_Returns409Not500_BUG047()
    {
        var (tc, cu) = Principals();

        await using (var seed = CreateContext(tc, cu))
        {
            await seed.Database.MigrateAsync();
            await AssertOpenUniqueIndexExistsAsync(seed);
            await SeedEmployeeAsync(seed);
        }

        // Blocker: a competing open clock-in row, inserted but NOT yet committed. It holds the unique
        // key (tenant, employee, clock_out IS NULL) but is invisible to READ COMMITTED readers.
        await using var blockerDb = CreateContext(tc, cu);
        await using var blockerTx = await blockerDb.Database.BeginTransactionAsync();
        blockerDb.AttendanceLogs.Add(new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeId = _employeeId,
            ClockIn = DateTime.UtcNow,
            Source = "WEB",
        });
        await blockerDb.SaveChangesAsync(); // INSERT executes inside the tx; still uncommitted.

        // The "second" concurrent clock-in via the real service on its own connection. Started (not
        // awaited) so it runs alongside our orchestration: its BR-1 pre-check passes (blocker unseen),
        // then its INSERT blocks on the unique key.
        await using var svcDb = CreateContext(tc, cu);
        var clockInTask = BuildService(svcDb, tc, cu)
            .ClockInAsync(new ClockInData { Source = "WEB", IpAddress = "10.0.0.1" });

        await using (var probe = CreateContext(tc, cu))
            await WaitUntilClockInInsertBlockedAsync(probe, TimeSpan.FromSeconds(20));

        // Release the blocker → its open row wins → the service's blocked INSERT trips 23505.
        await blockerTx.CommitAsync();

        var result = await clockInTask;

        // Post-fix: clean 409. Pre-fix: `await clockInTask` throws DbUpdateException (the 500) here.
        result.IsFailure.Should().BeTrue("a concurrent duplicate clock-in is a 409, not an unhandled 500");
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("already_clocked_in");

        // The losing clock-in wrote NO second open row — exactly one open record (the blocker's) exists.
        await using var verify = CreateContext(tc, cu);
        (await verify.AttendanceLogs.CountAsync(a => a.EmployeeId == _employeeId && a.ClockOut == null))
            .Should().Be(1, "the conflicting insert (and its audit row) must roll back with the failed transaction");
    }

    // ── Positive control: an uncontended clock-in still succeeds on Postgres ──
    [Fact]
    public async Task ClockIn_NoContention_Succeeds_BUG047()
    {
        var (tc, cu) = Principals();

        await using (var seed = CreateContext(tc, cu))
        {
            await seed.Database.MigrateAsync();
            await SeedEmployeeAsync(seed);
        }

        await using var svcDb = CreateContext(tc, cu);
        var result = await BuildService(svcDb, tc, cu)
            .ClockInAsync(new ClockInData { Source = "WEB", IpAddress = "10.0.0.1" });

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.EmployeeId.Should().Be(_employeeId);
        result.Value.ClockOut.Should().BeNull();

        await using var verify = CreateContext(tc, cu);
        (await verify.AttendanceLogs.CountAsync(a => a.EmployeeId == _employeeId && a.ClockOut == null))
            .Should().Be(1);
    }
}
