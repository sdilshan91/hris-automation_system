// ============================================================================
// DF-44 / US-ATT-008 / DF-33 — the chronic-lateness escalation CROSSING (shipped #385), exercised on REAL
// PostgreSQL through the real ClockInAsync path (AttendanceService + real LateEarlyService / ShiftService /
// OvertimeService). Now DETERMINISTIC because AttendanceService takes an injected TimeProvider (DF-43,
// trailing-optional `TimeProvider? timeProvider = null`) — we pass a fixed FakeTimeProvider so the punch's
// UTC instant (and therefore "this month" / "this local day") is pinned regardless of when the suite runs.
//
// WHY POSTGRES (not InMemory):
//   Arm 1 — LateEarlyService.CountLateDaysInMonthAsync runs a UTC month-range scan of attendance_log and a
//           DISTINCT-late-day count; this arm proves that month-range date query translates on Npgsql (not
//           just LINQ-to-objects over an InMemory dictionary) and that the crossing fires exactly at
//           threshold+1.
//   Arm 2 — HasOtherLateLogOnLocalDateAsync loads late logs in a ±1-day UTC WINDOW and compares TENANT-LOCAL
//           dates. Two late logs on the same LOCAL date but straddling a UTC-day boundary (a non-UTC tenant)
//           must be treated as ONE day for the same-day re-fire guard. InMemory + client-eval can mask a
//           timezone-window bug; real Postgres date arithmetic on a timestamptz column cannot.
//
// The crossing condition implemented in AttendanceService.ClockInAsync is:
//   fire  ⇔  log.IsLate  ∧  ChronicThreshold > 0
//            ∧  monthToDate distinct late days == ChronicThreshold + 1   (the exact crossing)
//            ∧  this punch is the FIRST late log of that tenant-local day (no same-day re-fire)
//
// The notification service is an NSubstitute so we can assert exactly when (and whether) the escalation fires.
// Harness copied from AttendanceSettingsCrudPostgresTests; the internal FakeTimeProvider is reused from
// HRM.Tests.Unit (NotificationPreferenceServiceTests.cs — same assembly). UseSnakeCaseNamingConvention() is
// NOT optional (omitting it makes MigrateAsync throw PendingModelChangesWarning).
//
// Traceability: @TC-ATT-161 (chronic-lateness crossing) against the real provider.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit;                 // internal FakeTimeProvider (same assembly)
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class AttendanceChronicLatenessPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(Guid.NewGuid(), Guid.NewGuid());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; init; }
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
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private AppDbContext Db(Guid tenantId, Guid userId, ITenantContext? tc = null)
    {
        tc ??= new FixedTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(userId);
        cu.Email.Returns("emp@acme.test");

        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
                .Options,
            tc);
    }

    /// <summary>
    /// Builds an AttendanceService over its own fresh DbContext (shared with the real sub-services), a fixed
    /// clock and the supplied NSubstitute notifications. The current user is linked to <paramref name="userId"/>
    /// so ClockInAsync resolves the seeded employee.
    /// </summary>
    private (AttendanceService Service, AppDbContext Db) BuildService(
        Guid tenantId, Guid userId, IAttendanceNotificationService notifications, TimeProvider clock)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        var db = Db(tenantId, userId, tc);

        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(userId);
        cu.Email.Returns("emp@acme.test");

        var overtime = new OvertimeService(db, tc, cu, NullLogger<OvertimeService>.Instance);
        var shift = new ShiftService(db, tc, cu, NullLogger<ShiftService>.Instance);
        var lateEarly = new LateEarlyService(db, tc, cu, NullLogger<LateEarlyService>.Instance);

        var service = new AttendanceService(
            db, tc, cu, overtime, shift, NullLogger<AttendanceService>.Instance,
            workflowRuntime: null, notifications: notifications, lateEarly: lateEarly, timeProvider: clock);

        return (service, db);
    }

    private LateEarlyService LateEarly(Guid tenantId, Guid userId)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        var db = Db(tenantId, userId, tc);
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(userId);
        return new LateEarlyService(db, tc, cu, NullLogger<LateEarlyService>.Instance);
    }

    private static ClockInData Data() =>
        new() { Source = "WEB", IpAddress = "10.0.0.1", UserAgent = "Chrome/120" };

    private static DateTime Utc(int y, int m, int d, int h, int min) =>
        new(y, m, d, h, min, 0, DateTimeKind.Utc);

    // ── seeding ─────────────────────────────────────────────────────────

    private static Guid NewEmployee(AppDbContext db, Guid tenantId, Guid userId, string no)
    {
        var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = $"D{no}", Code = no };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, TitleName = $"T{no}" };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);

        // Postgres enforces fk_employees_users_user_id (InMemory does not) — seed the linked User row.
        db.Users.Add(new User { Id = userId, Email = $"{no}@acme.test" });

        var id = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = id, TenantId = tenantId, UserId = userId, EmployeeNo = no, FirstName = no, LastName = "W",
            Email = $"{no}@acme.test", DepartmentId = dept.Id, JobTitleId = title.Id,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        return id;
    }

    /// <summary>An all-day default fixed shift (00:00–23:59, zero grace) so any real clock-in is late.</summary>
    private static void AddDefaultAllDayShift(AppDbContext db, Guid tenantId) =>
        db.Shifts.Add(new Shift
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "All-day",
            Type = ShiftType.Single, StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(23, 59),
            GracePeriodMinutes = 0, IsDefault = true,
        });

    private static void AddLatePolicy(AppDbContext db, Guid tenantId, int chronicThreshold) =>
        db.LatePolicies.Add(new LatePolicy
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
            ChronicThreshold = chronicThreshold, IsActive = true,
        });

    private static void AddClosedLateLog(AppDbContext db, Guid tenantId, Guid employeeId, DateTime clockInUtc) =>
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            ClockIn = clockInUtc, ClockOut = clockInUtc.AddHours(8),
            IsLate = true, LateMinutes = 30, Source = "WEB",
        });

    // ══ Arm 1 — month-range crossing count on real Postgres ══

    /// <summary>
    /// Five prior distinct late days in June 2026 + a real late clock-in on the 15th = SIX distinct late days
    /// (== threshold 5 + 1): the exact crossing. The escalation must fire exactly once with lateCount 6, and
    /// CountLateDaysInMonthAsync (the UTC month-range DISTINCT-day scan) must return 6 on Npgsql.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public async Task ClockIn_AtCrossing_FiresOnce_AndMonthCountIsThresholdPlusOne()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(Utc(2026, 6, 15, 10, 0)));
        var notifications = Substitute.For<IAttendanceNotificationService>();
        Guid empId;

        await using (var seed = Db(tenantId, userId))
        {
            empId = NewEmployee(seed, tenantId, userId, "CRX1");
            seed.Tenants.Add(new Tenant { Id = tenantId, Subdomain = "utc-" + tenantId.ToString("N")[..8], Name = "UTC", TimeZone = "UTC" });
            AddDefaultAllDayShift(seed, tenantId);
            AddLatePolicy(seed, tenantId, chronicThreshold: 5);
            foreach (var day in new[] { 1, 2, 3, 4, 5 })    // 5 prior distinct late days (not the 15th)
                AddClosedLateLog(seed, tenantId, empId, Utc(2026, 6, day, 10, 0));
            await seed.SaveChangesAsync();
        }

        var (service, db) = BuildService(tenantId, userId, notifications, clock);
        await using (db)
        {
            var result = await service.ClockInAsync(Data());
            result.IsSuccess.Should().BeTrue(result.Error);
        }

        // The escalation fired exactly once, at the crossing (6 == threshold 5 + 1).
        await notifications.Received(1).NotifyChronicLatenessAsync(
            empId, 6, 5, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());

        // ...and the month-range DISTINCT-day scan translates on Npgsql and returns 6.
        (await LateEarly(tenantId, userId).CountLateDaysInMonthAsync(empId, new DateOnly(2026, 6, 15)))
            .Should().Be(6);
    }

    /// <summary>
    /// Four prior distinct late days + the punch = FIVE (== threshold 5, not threshold+1): below the crossing.
    /// The escalation must NOT fire, and the month-range count must be 5. Makes the exact-crossing arithmetic
    /// in the arm above load-bearing (a "== threshold" mutant would fire here).
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public async Task ClockIn_BelowThreshold_DoesNotFire_AndMonthCountIsThreshold()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clock = new FakeTimeProvider(new DateTimeOffset(Utc(2026, 6, 15, 10, 0)));
        var notifications = Substitute.For<IAttendanceNotificationService>();
        Guid empId;

        await using (var seed = Db(tenantId, userId))
        {
            empId = NewEmployee(seed, tenantId, userId, "CRX2");
            seed.Tenants.Add(new Tenant { Id = tenantId, Subdomain = "utc-" + tenantId.ToString("N")[..8], Name = "UTC", TimeZone = "UTC" });
            AddDefaultAllDayShift(seed, tenantId);
            AddLatePolicy(seed, tenantId, chronicThreshold: 5);
            foreach (var day in new[] { 1, 2, 3, 4 })       // 4 prior distinct late days
                AddClosedLateLog(seed, tenantId, empId, Utc(2026, 6, day, 10, 0));
            await seed.SaveChangesAsync();
        }

        var (service, db) = BuildService(tenantId, userId, notifications, clock);
        await using (db)
        {
            (await service.ClockInAsync(Data())).IsSuccess.Should().BeTrue();
        }

        await notifications.DidNotReceive().NotifyChronicLatenessAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());

        (await LateEarly(tenantId, userId).CountLateDaysInMonthAsync(empId, new DateOnly(2026, 6, 15)))
            .Should().Be(5);
    }

    // ══ Arm 2 — local-date same-day guard across a UTC boundary ══

    /// <summary>
    /// A +05:30 tenant (Asia/Kolkata). The local date 2026-06-15 straddles UTC: 2026-06-14 20:00Z is local
    /// 2026-06-15 01:30, and the punch at 2026-06-15 02:00Z is local 2026-06-15 07:30 — the SAME local date,
    /// different UTC dates. With a third late day (06-10) the UTC-distinct month count reaches 3 == threshold
    /// 2 + 1, so the crossing ARITHMETIC is satisfied — yet the escalation must NOT fire, because
    /// HasOtherLateLogOnLocalDateAsync finds the earlier late log on the SAME tenant-local date (the ±1-day
    /// UTC window + local-date compare must work on real Postgres). Only the same-day guard suppresses it.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-161")]
    public async Task ClockIn_SecondLateLog_SameLocalDate_AcrossUtcBoundary_DoesNotReFire()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        // Punch at 2026-06-15 02:00Z → local 2026-06-15 07:30 in Asia/Kolkata (+05:30).
        var clock = new FakeTimeProvider(new DateTimeOffset(Utc(2026, 6, 15, 2, 0)));
        var notifications = Substitute.For<IAttendanceNotificationService>();
        Guid empId;

        await using (var seed = Db(tenantId, userId))
        {
            empId = NewEmployee(seed, tenantId, userId, "TZ1");
            seed.Tenants.Add(new Tenant
            {
                Id = tenantId, Subdomain = "kol-" + tenantId.ToString("N")[..8],
                Name = "Kolkata", TimeZone = "Asia/Kolkata",
            });
            AddDefaultAllDayShift(seed, tenantId);
            AddLatePolicy(seed, tenantId, chronicThreshold: 2);

            // A prior late log on the SAME local date (2026-06-15) but the PREVIOUS UTC date (06-14 20:00Z
            // → local 06-15 01:30). This is the log the same-day guard must find via its UTC window.
            AddClosedLateLog(seed, tenantId, empId, Utc(2026, 6, 14, 20, 0));
            // A third distinct-UTC-day late log so the UTC month-distinct count reaches threshold+1 (== 3).
            AddClosedLateLog(seed, tenantId, empId, Utc(2026, 6, 10, 10, 0));
            await seed.SaveChangesAsync();
        }

        var (service, db) = BuildService(tenantId, userId, notifications, clock);
        await using (db)
        {
            (await service.ClockInAsync(Data())).IsSuccess.Should().BeTrue();
        }

        // The crossing arithmetic IS satisfied (3 distinct UTC late days == threshold 2 + 1)...
        (await LateEarly(tenantId, userId).CountLateDaysInMonthAsync(empId, new DateOnly(2026, 6, 15)))
            .Should().Be(3);

        // ...but the punch is NOT the first late log of the tenant-LOCAL day, so no escalation fires.
        await notifications.DidNotReceive().NotifyChronicLatenessAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }
}
