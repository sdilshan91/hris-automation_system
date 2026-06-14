// ============================================================================
// US-ATT-003: Attendance regularization integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (MediatR
// pipeline + ITenantContext-driven global query filters), covering:
//   - SubmitRegularizationCommand happy path end-to-end (AC-1)
//   - the employee's own regularization list query (§8)
//   - Tenant isolation: a regularization submitted in Tenant A is invisible to Tenant B, and the
//     duplicate-pending rule (AC-4/BR-3) does NOT bleed across tenants.
//
// NOTE ON PROVIDER: identical rationale to AttendanceIntegrationTests / LeaveRequestIntegrationTests —
// the verify gate runs `dotnet test` with NO PostgreSQL bound and Docker is unavailable in the agent
// sandbox, so a Testcontainers test would red the gate. These tests use the InMemory provider through
// the real composed pipeline (MediatR + tenant query filters), which is what proves tenant isolation.
// PostgreSQL-specific schema (partial unique index, date, FKs) is validated by the `migrations` CI job.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Attendance.Commands;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Application.Features.Attendance.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class AttendanceRegularizationIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    private Guid _employeeA;
    private Guid _employeeB;

    public AttendanceRegularizationIntegrationTests()
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
            cfg.RegisterServicesFromAssembly(typeof(SubmitRegularizationCommand).Assembly));

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

    // Corrected times are wall-clock "HH:mm" strings; the backend combines date + HH:mm into UTC.
    private static SubmitRegularizationCommand Command(DateOnly date)
        => new(new SubmitRegularizationRequest
        {
            Date = date,
            RegularizationType = RegularizationType.MissedBoth,
            RequestedClockIn = "09:00",
            RequestedClockOut = "17:00",
            Reason = "Forgot to clock in and out on this day",
        });

    // ── AC-1: happy path end-to-end ─────────────────────────────────────────

    [Fact]
    public async Task Submit_HappyPath_PersistsPendingRecord()
    {
        var (mediator, provider) = BuildPipeline(_tenantA, _userA);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        var result = await mediator.Send(Command(date));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(RegularizationStatus.Pending);
        result.Value.EmployeeId.Should().Be(_employeeA);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AttendanceRegularizations.Should().ContainSingle(r => r.EmployeeId == _employeeA);
    }

    // ── §8: list query ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyRegularizations_ReturnsOwnRequest()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        await mediator.Send(Command(date));

        var list = await mediator.Send(new GetMyRegularizationsQuery());

        list.IsSuccess.Should().BeTrue();
        list.Value!.Should().ContainSingle();
        list.Value[0].Date.Should().Be(date);
    }

    // ── Tenant isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_RecordIsScopedToActingTenant()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        var (mediatorA, _) = BuildPipeline(_tenantA, _userA);
        (await mediatorA.Send(Command(date))).IsSuccess.Should().BeTrue();

        // Tenant B submits for the SAME date and employee-of-its-own — must succeed (the
        // duplicate-pending rule AC-4/BR-3 is tenant-scoped, so A's record does not block B).
        var (mediatorB, providerB) = BuildPipeline(_tenantB, _userB);
        var resultB = await mediatorB.Send(Command(date));
        resultB.IsSuccess.Should().BeTrue();

        // Tenant B's filtered context sees only its own record, not Tenant A's.
        using var scope = providerB.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var visible = db.AttendanceRegularizations.ToList();
        visible.Should().ContainSingle();
        visible[0].EmployeeId.Should().Be(_employeeB);
        visible.Should().NotContain(r => r.EmployeeId == _employeeA);
    }
}
