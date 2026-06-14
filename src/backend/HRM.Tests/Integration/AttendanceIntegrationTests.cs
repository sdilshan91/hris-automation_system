// ============================================================================
// US-ATT-001: Attendance clock-in integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (with the MediatR pipeline + ITenantContext-driven global query
// filters), covering:
//   - POST /api/v1/attendance/clock-in happy path (ClockInCommand end-to-end, AC-1)
//   - duplicate clock-in rejection (AC-2/BR-1)
//   - Tenant isolation: an employee in Tenant A's clock-in lands only in Tenant A,
//     and Tenant B cannot see it.
//
// NOTE ON PROVIDER: identical rationale to LeaveRequestIntegrationTests — the repo's
// verify gate runs `dotnet test` with NO PostgreSQL bound and Docker is unavailable in
// the agent sandbox, so a Testcontainers-backed test would red the gate it must pass.
// These tests use the same InMemory provider as the rest of the suite but go through
// the real composed pipeline (MediatR + tenant query filters), which is what proves
// tenant isolation. The PostgreSQL-specific schema (numeric(10,7), text[], partial
// unique indexes, FKs) is validated by the `migrations` CI job applying the generated
// migration to real PostgreSQL.
// ============================================================================

using FluentAssertions;
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

public sealed class AttendanceIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    private Guid _employeeA;
    private Guid _employeeB;

    public AttendanceIntegrationTests()
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
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ClockInCommand).Assembly));

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

    private static ClockInCommand Command(string ip = "10.0.0.1")
        => new(null, null, null, "WEB", ip, "IntegrationTest/1.0", null);

    // ── AC-1: happy path end-to-end ────────────────────────────────

    [Fact]
    public async Task ClockIn_HappyPath_PersistsOpenRecord()
    {
        var (mediator, provider) = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Command());

        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeeId.Should().Be(_employeeA);
        result.Value.ClockOut.Should().BeNull();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AttendanceLogs.Should().ContainSingle(a => a.EmployeeId == _employeeA);
    }

    // ── AC-2 / BR-1: duplicate clock-in ────────────────────────────

    [Fact]
    public async Task ClockIn_Twice_SecondIsRejected()
    {
        var (mediator, _) = BuildPipeline(_tenantA, _userA);

        (await mediator.Send(Command())).IsSuccess.Should().BeTrue();
        var second = await mediator.Send(Command());

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
    }

    // ── Tenant isolation ───────────────────────────────────────────

    [Fact]
    public async Task ClockIn_RecordIsScopedToActingTenant()
    {
        var (mediatorA, _) = BuildPipeline(_tenantA, _userA);
        await mediatorA.Send(Command());

        // Tenant B clocks in its own employee.
        var (mediatorB, providerB) = BuildPipeline(_tenantB, _userB);
        var resultB = await mediatorB.Send(Command());
        resultB.IsSuccess.Should().BeTrue();

        // Tenant B's filtered context sees only its own record, not Tenant A's.
        using var scope = providerB.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var visible = db.AttendanceLogs.ToList();
        visible.Should().ContainSingle();
        visible[0].EmployeeId.Should().Be(_employeeB);
        visible.Should().NotContain(a => a.EmployeeId == _employeeA);
    }
}
