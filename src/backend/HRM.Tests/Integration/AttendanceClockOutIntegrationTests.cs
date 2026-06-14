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
        // Opened 2h ago (within today) — must not be auto-closed.
        var logId = SeedOpenLog(_tenantA, _employeeA, agoMinutes: 120);
        SeedTenantRow(_tenantA);

        var provider = BuildJobProvider();
        var job = new AutoClockOutJob(provider.GetRequiredService<IServiceScopeFactory>());

        await job.RunAsync();

        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        db.AttendanceLogs.Single(a => a.Id == logId).ClockOut.Should().BeNull();
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
        return services.BuildServiceProvider();
    }
}
