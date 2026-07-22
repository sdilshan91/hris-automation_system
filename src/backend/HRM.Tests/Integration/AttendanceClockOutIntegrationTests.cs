// ============================================================================
// US-ATT-002: Attendance clock-out integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container
// (MediatR pipeline + ITenantContext-driven global query filters), covering:
//   - POST /api/v1/attendance/clock-out happy path with correct total (AC-1)
//   - no-open-record rejection (AC-2/BR-1)
//   - multi-tenant isolation: Tenant B cannot clock out Tenant A's open record
//   - the AutoClockOutJob (BR-5) closing an open record left open overnight
//
// NOTE ON PROVIDER: identical rationale to AttendanceIntegrationTests / LeaveRequestIntegrationTests
// — the verify gate runs `dotnet test` with NO PostgreSQL bound and Docker is unavailable, so a
// Testcontainers-backed test would red the gate. These use the InMemory provider through the real
// composed pipeline, which is what proves tenant isolation. PostgreSQL-specific schema is validated
// by the `migrations` CI job.
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.Commands;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class AttendanceClockOutIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    private Guid _employeeA;
    private Guid _employeeB;

    public AttendanceClockOutIntegrationTests()
    {
        SeedTwoTenants();
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "test";
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

    private (IMediator Mediator, IServiceProvider Provider) BuildPipeline(Guid tenantId, Guid userId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        // AttendanceService now depends on IOvertimeService for clock-out auto-detection (US-ATT-006).
        services.AddScoped<IOvertimeService, OvertimeService>();
        // US-ATT-008: AttendanceService now also depends on IShiftService for inline late/early detection.
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ClockOutCommand).Assembly));

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMediator>(), provider);
    }

    private void SeedTwoTenants()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _employeeA = Guid.NewGuid();
        _employeeB = Guid.NewGuid();

        db.Employees.AddRange(
            new Employee
            {
                Id = _employeeA, TenantId = _tenantA, UserId = _userA,
                EmployeeNo = "A-1", FirstName = "Alice", LastName = "A", Email = "a@a.com",
                DateOfJoining = new DateTime(2020, 1, 1),
                DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            },
            new Employee
            {
                Id = _employeeB, TenantId = _tenantB, UserId = _userB,
                EmployeeNo = "B-1", FirstName = "Bob", LastName = "B", Email = "b@b.com",
                DateOfJoining = new DateTime(2020, 1, 1),
                DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            });

        db.SaveChanges();
    }

    /// <summary>Seeds an open clock-in for an employee in the given tenant, clocked in N minutes ago.</summary>
    private Guid SeedOpenLog(Guid tenantId, Guid employeeId, int agoMinutes)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        var id = BaseEntity.NewUuidV7();
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = id,
            TenantId = tenantId,
            EmployeeId = employeeId,
            ClockIn = DateTime.UtcNow.AddMinutes(-agoMinutes),
            Source = "WEB",
        });
        db.SaveChanges();
        return id;
    }

    private static ClockOutCommand Command(decimal? lat = null, decimal? lon = null, string ip = "10.0.0.1")
        => new(lat, lon, ip, "IntegrationTest/1.0");

    // ── AC-1: happy path end-to-end ────────────────────────────────

    [Fact]
    public async Task ClockOut_HappyPath_ClosesRecordWithComputedTotal()
    {
        SeedOpenLog(_tenantA, _employeeA, agoMinutes: 480);   // 8h
        var (mediator, provider) = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Command());

        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeeId.Should().Be(_employeeA);
        result.Value.TotalWorkMinutes.Should().Be(420);      // 480 - 60 break
        result.Value.Status.Should().Be("COMPLETE");

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AttendanceLogs.Single().ClockOut.Should().NotBeNull();
    }

    // ── AC-2 / BR-1: no open record ────────────────────────────────

    [Fact]
    public async Task ClockOut_NoOpenRecord_IsRejected()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Command());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Multi-tenant isolation ─────────────────────────────────────

    [Fact]
    public async Task ClockOut_CannotCloseAnotherTenantsOpenRecord()
    {
        // Tenant A has an open record; Tenant B's employee tries to clock out and must be rejected
        // (B has no open record of its own — A's record is invisible behind the query filter).
        SeedOpenLog(_tenantA, _employeeA, agoMinutes: 480);

        var (mediatorB, _) = BuildPipeline(_tenantB, _userB);
        var result = await mediatorB.Send(Command());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);

        // A's record remains open.
        var (mediatorA, providerA) = BuildPipeline(_tenantA, _userA);
        using var scope = providerA.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AttendanceLogs.Single(a => a.EmployeeId == _employeeA).ClockOut.Should().BeNull();
    }

    // ── BR-5: auto-clock-out job ───────────────────────────────────

    [Fact]
    public async Task AutoClockOutJob_ClosesOvernightOpenRecord_AsAnomaly()
    {
        // A record opened a day-and-a-bit ago (i.e. before today's UTC start) is left open.
        var minutesAgo = (int)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMinutes + (10 * 60); // before midnight
        var logId = SeedOpenLog(_tenantA, _employeeA, agoMinutes: minutesAgo);

        // Seed the tenant row so the job's tenant scan finds Tenant A.
        SeedTenantRow(_tenantA);

        var provider = BuildJobProvider();
        var job = new AutoClockOutJob(provider.GetRequiredService<IServiceScopeFactory>());

        await job.RunAsync();

        // Verify via a tenant-A-scoped context.
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        var closed = db.AttendanceLogs.Single(a => a.Id == logId);
        closed.ClockOut.Should().NotBeNull();
        closed.Status.Should().Be("ANOMALY");
        closed.TotalWorkMinutes.Should().NotBeNull();
    }

    [Fact]
    public async Task AutoClockOutJob_LeavesTodaysOpenRecordUntouched()
    {
        // Opened earlier TODAY (after this UTC day's start) — must not be auto-closed.
        // Clamp the offset to minutes-since-midnight so the seed never crosses into the
        // previous UTC day when the suite runs shortly after midnight (de-flake; mirrors
        // the inverse computation in AutoClockOutJob_ClosesOvernightOpenRecord_AsAnomaly).
        var minutesSinceMidnight = (int)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMinutes;
        var logId = SeedOpenLog(_tenantA, _employeeA, agoMinutes: Math.Min(120, minutesSinceMidnight));
        SeedTenantRow(_tenantA);

        var provider = BuildJobProvider();
        var job = new AutoClockOutJob(provider.GetRequiredService<IServiceScopeFactory>());

        await job.RunAsync();

        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        db.AttendanceLogs.Single(a => a.Id == logId).ClockOut.Should().BeNull();
    }

    // ── DF-22 / ISSUE-309: per-location policy in the auto-clock-out sweep ──

    /// <summary>
    /// DF-22 / ISSUE-309: the auto-clock-out sweep must resolve the attendance policy PER LOCATION, not apply
    /// the tenant default to every open log. Two employees — one at a Dubai location whose override sets
    /// AutoBreakMinutes=120, one at the tenant default (AutoBreakMinutes=60) — are both auto-closed in the
    /// SAME RunAsync. With an identical gross span, the computed TotalWorkMinutes must differ by the break
    /// delta, proving each log's calc used ITS location's policy. Before the fix both used the tenant default
    /// and both totals would be identical.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-162")]
    public async Task AutoClockOutJob_ResolvesPolicyPerLocation_AcrossTwoLocationsInOneSweep_DF22()
    {
        var dubaiId = Guid.NewGuid();
        var dubaiEmp = Guid.NewGuid();

        // Seed a Dubai location + a Dubai employee, plus the tenant-default and Dubai-override settings rows.
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using (var db = new AppDbContext(options, ctx))
        {
            db.Locations.Add(new Location
            {
                Id = dubaiId, TenantId = _tenantA, Name = "Dubai", TimeZone = "UTC", IsActive = true,
            });
            db.Employees.Add(new Employee
            {
                Id = dubaiEmp, TenantId = _tenantA, UserId = Guid.NewGuid(),
                EmployeeNo = "DXB-1", FirstName = "Dana", LastName = "D", Email = "d@dxb.com",
                DateOfJoining = new DateTime(2020, 1, 1),
                DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active,
                IsActive = true, LocationId = dubaiId,
            });
            // Tenant default: auto-break 60 min beyond the 180-min threshold.
            db.AttendanceSettings.Add(new AttendanceSettings
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA,
                AutoBreakThresholdMinutes = 180, AutoBreakMinutes = 60,
            });
            // Dubai override: a longer auto-break (120 min) — this is the discriminating field.
            db.AttendanceSettings.Add(new AttendanceSettings
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, LocationId = dubaiId,
                AutoBreakThresholdMinutes = 180, AutoBreakMinutes = 120,
            });
            db.SaveChanges();
        }

        // Open logs both clocked in yesterday at 14:00 UTC → the job closes them at yesterday 23:59:59, a
        // fixed gross span of 599 minutes (floor of 9h59m59s), independent of the wall clock at run time.
        var clockInUtc = DateTime.UtcNow.Date.AddDays(-1).AddHours(14);
        var dubaiLogId = SeedOpenLogAt(_tenantA, dubaiEmp, clockInUtc);
        var defaultLogId = SeedOpenLogAt(_tenantA, _employeeA, clockInUtc);

        SeedTenantRow(_tenantA);

        var provider = BuildJobProvider();
        var job = new AutoClockOutJob(provider.GetRequiredService<IServiceScopeFactory>());
        await job.RunAsync();

        var verifyCtx = new MutableTenantContext { TenantId = _tenantA };
        using var verify = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, verifyCtx);

        var dubaiLog = verify.AttendanceLogs.Single(a => a.Id == dubaiLogId);
        var defaultLog = verify.AttendanceLogs.Single(a => a.Id == defaultLogId);

        // Both auto-closed as anomalies (BR-5, isSystemClosed).
        dubaiLog.Status.Should().Be("ANOMALY");
        defaultLog.Status.Should().Be("ANOMALY");

        // gross = 599; default break 60 → 539; Dubai override break 120 → 479.
        defaultLog.TotalWorkMinutes.Should().Be(539, "the default-location employee used the tenant default");
        dubaiLog.TotalWorkMinutes.Should().Be(
            479, "the Dubai employee's calc used the location override's larger auto-break");
    }

    /// <summary>
    /// DF-22 / ISSUE-309 (enforcer safety property): the per-log location lookup must NOT drop an open log
    /// whose owning employee row is missing/deleted. Such a log resolves to a null location → the tenant
    /// default → (with NO settings rows at all) the code default — and is still auto-closed, never skipped.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-162")]
    public async Task AutoClockOutJob_OpenLogWithMissingEmployee_IsStillClosed_ViaCodeDefault_DF22()
    {
        // An open log for an employee id that has NO Employee row (and NO AttendanceSettings rows at all,
        // so For(map, null) returns null → the code-default AttendanceSettings fallback).
        var orphanEmp = Guid.NewGuid();
        var clockInUtc = DateTime.UtcNow.Date.AddDays(-1).AddHours(14);
        var orphanLogId = SeedOpenLogAt(_tenantA, orphanEmp, clockInUtc);

        SeedTenantRow(_tenantA);

        var provider = BuildJobProvider();
        var job = new AutoClockOutJob(provider.GetRequiredService<IServiceScopeFactory>());
        await job.RunAsync();

        var verifyCtx = new MutableTenantContext { TenantId = _tenantA };
        using var verify = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, verifyCtx);

        var orphanLog = verify.AttendanceLogs.Single(a => a.Id == orphanLogId);
        // Not silently dropped by the location lookup: closed as an anomaly with the code-default policy.
        orphanLog.ClockOut.Should().NotBeNull("the sweep must still close a log whose employee row is missing");
        orphanLog.Status.Should().Be("ANOMALY");
    }

    /// <summary>
    /// DF-63: the auto-clock-out job must apply DF-56's explicit per-shift work-minute knobs to the minutes it
    /// stores for a system-closed session — not just the tenant/location policy. The employee resolves (via the
    /// FR-5 tenant-default shift) to a shift whose AutoBreakMinutes override (120) differs from the tenant policy
    /// (60); with a fixed 599-min gross span the auto-closed TotalWorkMinutes must reflect the SHIFT's break
    /// (599 - 120 = 479), NOT the tenant's (599 - 60 = 539). Before DF-63 the job passed no shift and stored 539.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-162")]
    public async Task AutoClockOutJob_AppliesResolvedShiftKnobs_ToSystemClosedMinutes_DF63()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using (var db = new AppDbContext(options, ctx))
        {
            // Tenant default policy: auto-break 60 min beyond a 180-min threshold (the pre-DF-63 result).
            db.AttendanceSettings.Add(new AttendanceSettings
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA,
                AutoBreakThresholdMinutes = 180, AutoBreakMinutes = 60,
            });
            // FR-5 tenant-default shift carrying an EXPLICIT larger auto-break (120) — the discriminating knob.
            // Its AutoBreakThresholdMinutes is left null, so the threshold still falls back to the tenant's 180.
            db.Shifts.Add(new Shift
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, Name = "General",
                Type = ShiftType.Single, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0),
                WorkingDays = new List<int> { 1, 2, 3, 4, 5 }, IsDefault = true, IsActive = true,
                AutoBreakMinutes = 120,
            });
            db.SaveChanges();
        }

        // Open log clocked in yesterday 14:00 UTC → closed at yesterday 23:59:59 → fixed 599-min gross span.
        var clockInUtc = DateTime.UtcNow.Date.AddDays(-1).AddHours(14);
        var logId = SeedOpenLogAt(_tenantA, _employeeA, clockInUtc);

        SeedTenantRow(_tenantA);

        var provider = BuildJobProvider();
        var job = new AutoClockOutJob(provider.GetRequiredService<IServiceScopeFactory>());
        await job.RunAsync();

        var verifyCtx = new MutableTenantContext { TenantId = _tenantA };
        using var verify = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, verifyCtx);

        var log = verify.AttendanceLogs.Single(a => a.Id == logId);
        log.Status.Should().Be("ANOMALY");
        log.TotalWorkMinutes.Should().Be(
            479, "the auto-close applied the resolved shift's 120-min auto-break, not the tenant's 60 (which would give 539)");
    }

    /// <summary>
    /// DF-63 (FR-7 assignment path): proves the auto-close resolves the RIGHT shift PER EMPLOYEE in one sweep —
    /// not just the tenant-default fallback. Employee X carries an explicit EmployeeShift assignment to a shift
    /// with AutoBreakMinutes=90 (→ 599 - 90 = 509); employee Y (no assignment) falls to the FR-5 default shift
    /// with AutoBreakMinutes=120 (→ 479). The tenant policy (60 → 539) is a third distinct value, so each of the
    /// three resolution outcomes maps to a unique number: if the assignment join broke, X would drop to the
    /// default (479) or tenant (539) and the 509 assertion reds.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-162")]
    public async Task AutoClockOutJob_ResolvesAssignedShiftPerEmployee_AcrossOneSweep_DF63()
    {
        var assignedEmp = Guid.NewGuid();
        var assignedShiftId = BaseEntity.NewUuidV7();

        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using (var db = new AppDbContext(options, ctx))
        {
            db.Employees.Add(new Employee
            {
                Id = assignedEmp, TenantId = _tenantA, UserId = Guid.NewGuid(),
                EmployeeNo = "ASG-1", FirstName = "Xavier", LastName = "X", Email = "x@x.com",
                DateOfJoining = new DateTime(2020, 1, 1),
                DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            });
            // Tenant policy (the neither-shift fallback): break 60 → 539.
            db.AttendanceSettings.Add(new AttendanceSettings
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA,
                AutoBreakThresholdMinutes = 180, AutoBreakMinutes = 60,
            });
            // FR-5 default shift (employee Y): break 120 → 479.
            db.Shifts.Add(new Shift
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, Name = "General",
                Type = ShiftType.Single, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0),
                WorkingDays = new List<int> { 1, 2, 3, 4, 5 }, IsDefault = true, IsActive = true,
                AutoBreakMinutes = 120,
            });
            // Non-default shift assigned to employee X: break 90 → 509 (the discriminating knob).
            db.Shifts.Add(new Shift
            {
                Id = assignedShiftId, TenantId = _tenantA, Name = "Assigned",
                Type = ShiftType.Single, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0),
                WorkingDays = new List<int> { 1, 2, 3, 4, 5 }, IsDefault = false, IsActive = true,
                AutoBreakMinutes = 90,
            });
            db.EmployeeShifts.Add(new EmployeeShift
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA,
                EmployeeId = assignedEmp, ShiftId = assignedShiftId,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-30)), EffectiveTo = null,
            });
            db.SaveChanges();
        }

        var clockInUtc = DateTime.UtcNow.Date.AddDays(-1).AddHours(14);   // fixed 599-min gross span
        var assignedLogId = SeedOpenLogAt(_tenantA, assignedEmp, clockInUtc);
        var defaultLogId = SeedOpenLogAt(_tenantA, _employeeA, clockInUtc);

        SeedTenantRow(_tenantA);

        var provider = BuildJobProvider();
        var job = new AutoClockOutJob(provider.GetRequiredService<IServiceScopeFactory>());
        await job.RunAsync();

        var verifyCtx = new MutableTenantContext { TenantId = _tenantA };
        using var verify = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, verifyCtx);

        var assignedLog = verify.AttendanceLogs.Single(a => a.Id == assignedLogId);
        var defaultLog = verify.AttendanceLogs.Single(a => a.Id == defaultLogId);

        assignedLog.Status.Should().Be("ANOMALY");
        defaultLog.Status.Should().Be("ANOMALY");
        assignedLog.TotalWorkMinutes.Should().Be(
            509, "employee X's calc used the ASSIGNED shift's 90-min break (not the 120 default → 479, nor tenant 60 → 539)");
        defaultLog.TotalWorkMinutes.Should().Be(
            479, "employee Y has no assignment → the FR-5 default shift's 120-min break");
    }

    /// <summary>Seeds an open clock-in for an employee at a fixed UTC instant (deterministic gross span).</summary>
    private Guid SeedOpenLogAt(Guid tenantId, Guid employeeId, DateTime clockInUtc)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        var id = BaseEntity.NewUuidV7();
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = id,
            TenantId = tenantId,
            EmployeeId = employeeId,
            ClockIn = DateTime.SpecifyKind(clockInUtc, DateTimeKind.Utc),
            Source = "WEB",
        });
        db.SaveChanges();
        return id;
    }

    private void SeedTenantRow(Guid tenantId)
    {
        // Tenants are not tenant-scoped; use a system-ish context. The Tenant entity has no global
        // filter on TenantId, so any context can write/read it.
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        if (!db.Tenants.IgnoreQueryFilters().Any(t => t.Id == tenantId))
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = $"Tenant {tenantId}",
                Subdomain = $"t{tenantId:N}".Substring(0, 12),
                Status = TenantStatus.Active,
            });
            db.SaveChanges();
        }
    }

    /// <summary>
    /// DI provider for the AutoClockOutJob: registers the concrete <see cref="TenantContext"/> as a
    /// scoped <see cref="ITenantContext"/> so the job's per-tenant scope can flip the acting tenant
    /// (the job casts to the concrete type and calls SetTenant).
    /// </summary>
    private IServiceProvider BuildJobProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        // DF-63: the job now resolves IShiftService to apply per-shift work-minute knobs to the auto-closed
        // minutes. ShiftService needs ICurrentUser; its resolution path is read-only so a bare substitute suffices.
        services.AddSingleton(Substitute.For<ICurrentUser>());
        services.AddScoped<IShiftService, ShiftService>();
        // RLS increment 2c: AutoClockOutJob wraps its per-tenant body in ITenantJobRunner — register the real
        // runner + an empty IConfiguration (Rls:Enabled defaults false → runs the work directly, no tx/GUC).
        services.AddScoped<ITenantJobRunner, TenantJobRunner>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }
}
