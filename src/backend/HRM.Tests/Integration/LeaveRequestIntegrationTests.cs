// ============================================================================
// US-LV-003: Leave Request integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (with the MediatR pipeline + global query filters), covering:
//   - POST /api/v1/leaves happy path (CreateLeaveRequestCommand end-to-end)
//   - Tenant isolation: an employee in Tenant A cannot create a request that
//     lands in Tenant B, and cannot see Tenant B's requests.
//
// NOTE ON PROVIDER: The repo's verify gate (.github/workflows/ci-gate.yml) runs
// `dotnet test` with NO PostgreSQL service bound to the test job (EF Core InMemory
// by design); the real-Postgres apply runs in a separate `migrations` job. Docker
// is also unavailable in the local agent sandbox. A Testcontainers-backed test would
// therefore red the gate it must pass. These integration tests use the same
// InMemory provider as the rest of the suite but go through the real composed
// pipeline (MediatR + ITenantContext-driven query filters), which is what proves
// tenant isolation. The PostgreSQL-specific schema (text[], partial index, FKs) is
// validated by the `migrations` CI job applying the generated migration to real PG.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.Commands;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Application.Features.LeaveRequests.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class LeaveRequestIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    private Guid _leaveTypeA;
    private Guid _leaveTypeB;
    private Guid _employeeA;
    private Guid _employeeB;

    public LeaveRequestIntegrationTests()
    {
        SeedTwoTenants();
    }

    // Mutable tenant context so we can switch the "current tenant" per request,
    // exactly as TenantResolutionMiddleware does in production.
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

    private IMediator BuildPipeline(Guid tenantId, Guid userId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        services.AddScoped<IHolidayProvider, NoOpHolidayProvider>();
        services.AddScoped<ILeaveNotificationService, LogOnlyLeaveNotificationService>();
        services.AddScoped<ILeaveRequestService, LeaveRequestService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(CreateLeaveRequestCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private void SeedTwoTenants()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _leaveTypeA = Guid.NewGuid();
        _leaveTypeB = Guid.NewGuid();
        _employeeA = Guid.NewGuid();
        _employeeB = Guid.NewGuid();

        db.LeaveTypes.AddRange(
            new LeaveType
            {
                Id = _leaveTypeA, TenantId = _tenantA, Name = "Annual Leave",
                AnnualEntitlement = 14, AccrualFrequency = AccrualFrequency.Upfront,
                Gender = LeaveTypeGender.All, IsActive = true,
            },
            new LeaveType
            {
                Id = _leaveTypeB, TenantId = _tenantB, Name = "Annual Leave",
                AnnualEntitlement = 14, AccrualFrequency = AccrualFrequency.Upfront,
                Gender = LeaveTypeGender.All, IsActive = true,
            });

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

        // Balances for both tenants' employees.
        foreach (var (tenant, emp, lt) in new[]
        {
            (_tenantA, _employeeA, _leaveTypeA),
            (_tenantB, _employeeB, _leaveTypeB),
        })
        {
            db.LeaveLedgerEntries.Add(new LeaveLedger
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenant,
                EntryType = LedgerEntryType.Accrual, EmployeeId = emp, LeaveTypeId = lt,
                LeaveYear = NextMonday().Year, Amount = 14m, BalanceAfter = 14m, OccurredAt = DateTime.UtcNow,
            });
        }

        db.SaveChanges();
    }

    private static DateOnly NextMonday()
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        while (d.DayOfWeek != DayOfWeek.Monday)
            d = d.AddDays(1);
        return d;
    }

    private static CreateLeaveRequestCommand Command(Guid leaveTypeId, DateOnly start, DateOnly end)
        => new(leaveTypeId, start, end, false, null, "Test", null);

    // ── Happy path (POST /api/v1/leaves end-to-end) ────────────────

    [Fact]
    public async Task CreateLeaveRequest_HappyPath_PersistsPendingRequest()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var monday = NextMonday();

        var result = await mediator.Send(Command(_leaveTypeA, monday, monday.AddDays(2)));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Pending");
        result.Value.TotalDays.Should().Be(3m);
        result.Value.EmployeeId.Should().Be(_employeeA);

        // Visible via GetMine.
        var mine = await mediator.Send(new GetMyLeaveRequestsQuery());
        mine.Value!.Should().ContainSingle(r => r.Id == result.Value.Id);
    }

    // ── Tenant isolation ───────────────────────────────────────────

    [Fact]
    public async Task EmployeeInTenantA_CannotCreateUsingTenantBLeaveType()
    {
        // Acting as Tenant A; reference a leave type that belongs to Tenant B.
        var mediator = BuildPipeline(_tenantA, _userA);
        var monday = NextMonday();

        var result = await mediator.Send(Command(_leaveTypeB, monday, monday.AddDays(2)));

        // The global query filter hides Tenant B's leave type from Tenant A.
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Leave type not found");
    }

    [Fact]
    public async Task GetMine_DoesNotLeakOtherTenantRequests()
    {
        var monday = NextMonday();

        // Tenant A creates a request.
        var medA = BuildPipeline(_tenantA, _userA);
        (await medA.Send(Command(_leaveTypeA, monday, monday.AddDays(1)))).IsSuccess.Should().BeTrue();

        // Tenant B creates a request.
        var medB = BuildPipeline(_tenantB, _userB);
        (await medB.Send(Command(_leaveTypeB, monday, monday.AddDays(1)))).IsSuccess.Should().BeTrue();

        // Each tenant only sees its own.
        var mineA = await medA.Send(new GetMyLeaveRequestsQuery());
        mineA.Value!.Should().OnlyContain(r => r.EmployeeId == _employeeA);

        var mineB = await medB.Send(new GetMyLeaveRequestsQuery());
        mineB.Value!.Should().OnlyContain(r => r.EmployeeId == _employeeB);
    }
}
