// ============================================================================
// US-LV-004: Pending Leave Queue integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (MediatR pipeline + ITenantContext-driven global query filters),
// covering:
//   - GET /api/v1/leaves/pending happy path: a manager sees their team's queued
//     requests (BR-1).
//   - Scope isolation: Manager A does not see Manager B's team (BR-1 / Test Hint).
//   - Tenant isolation: a manager in Tenant A does not see Tenant B requests.
//
// PROVIDER NOTE: same rationale as LeaveRequestIntegrationTests — the verify gate
// runs `dotnet test` with no PostgreSQL bound and Docker unavailable in the agent
// sandbox, so these use the InMemory provider but go through the real composed
// pipeline (MediatR + query filters), which is what proves scope/tenant isolation.
// The PostgreSQL-specific schema (partial index ix_leave_pending, FKs) is validated
// by the separate `migrations` CI job against real PG.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
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
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class PendingLeaveQueueIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    // Tenant A: managerA (Mary) -> report A1; manager2A (Mike) -> report A2.
    private readonly Guid _managerAUser = Guid.NewGuid();   // Mary
    private readonly Guid _manager2AUser = Guid.NewGuid();  // Mike
    private readonly Guid _managerBUser = Guid.NewGuid();   // Bea (Tenant B)

    private Guid _leaveTypeA;
    private Guid _leaveTypeB;
    private Guid _managerA;
    private Guid _manager2A;
    private Guid _reportA1;
    private Guid _reportA2;
    private Guid _managerB;
    private Guid _reportB1;

    public PendingLeaveQueueIntegrationTests()
    {
        SeedData();
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

    private void SeedData()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _leaveTypeA = Guid.NewGuid();
        _leaveTypeB = Guid.NewGuid();
        _managerA = Guid.NewGuid();
        _manager2A = Guid.NewGuid();
        _reportA1 = Guid.NewGuid();
        _reportA2 = Guid.NewGuid();
        _managerB = Guid.NewGuid();
        _reportB1 = Guid.NewGuid();

        db.LeaveTypes.AddRange(
            MakeLeaveType(_leaveTypeA, _tenantA),
            MakeLeaveType(_leaveTypeB, _tenantB));

        db.Employees.AddRange(
            Emp(_managerA, _tenantA, _managerAUser, "Mary", null),
            Emp(_manager2A, _tenantA, _manager2AUser, "Mike", null),
            Emp(_reportA1, _tenantA, null, "Ann", _managerA),
            Emp(_reportA2, _tenantA, null, "Abe", _manager2A),
            Emp(_managerB, _tenantB, _managerBUser, "Bea", null),
            Emp(_reportB1, _tenantB, null, "Ben", _managerB));

        // Queued (Pending-status) requests: one per report.
        db.LeaveRequests.AddRange(
            Queued(_reportA1, _leaveTypeA, _tenantA),
            Queued(_reportA2, _leaveTypeA, _tenantA),
            Queued(_reportB1, _leaveTypeB, _tenantB));

        db.SaveChanges();
    }

    private static readonly DateOnly Base = new(2026, 6, 1);

    private static LeaveType MakeLeaveType(Guid id, Guid tenantId) => new()
    {
        Id = id, TenantId = tenantId, Name = "Annual Leave", Color = "#4CAF50",
        AnnualEntitlement = 14, AccrualFrequency = AccrualFrequency.Upfront,
        Gender = LeaveTypeGender.All, IsActive = true,
    };

    private static Employee Emp(Guid id, Guid tenantId, Guid? userId, string first, Guid? reportsTo) => new()
    {
        Id = id, TenantId = tenantId, UserId = userId, EmployeeNo = $"E-{first}",
        FirstName = first, LastName = "X", Email = $"{first}@t.com".ToLowerInvariant(),
        DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = Guid.NewGuid(),
        JobTitleId = Guid.NewGuid(), EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active, ReportsToEmployeeId = reportsTo, IsActive = true,
    };

    private static LeaveRequest Queued(Guid empId, Guid leaveTypeId, Guid tenantId) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = empId,
        LeaveTypeId = leaveTypeId, StartDate = Base, EndDate = Base.AddDays(1),
        TotalDays = 2m, Status = LeaveRequestStatus.Pending, RequestedAt = DateTime.UtcNow,
    };

    private static GetPendingLeaveRequestsQuery Query() =>
        new(new PendingLeaveQueueQueryParams());

    // ── Happy path: manager sees own team's queued requests ────────

    [Fact]
    public async Task GetPending_HappyPath_ReturnsManagersTeamQueuedRequests()
    {
        var mediator = BuildPipeline(_tenantA, _managerAUser);

        var result = await mediator.Send(Query());

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Single().EmployeeId.Should().Be(_reportA1);
    }

    // ── Scope isolation: Manager A != Manager B's team (same tenant) ─

    [Fact]
    public async Task GetPending_DoesNotShowOtherManagersTeam()
    {
        var mary = BuildPipeline(_tenantA, _managerAUser);
        var mike = BuildPipeline(_tenantA, _manager2AUser);

        var maryQueue = await mary.Send(Query());
        var mikeQueue = await mike.Send(Query());

        maryQueue.Value!.Items.Should().OnlyContain(i => i.EmployeeId == _reportA1);
        mikeQueue.Value!.Items.Should().OnlyContain(i => i.EmployeeId == _reportA2);
    }

    // ── Tenant isolation: Tenant A manager cannot see Tenant B ──────

    [Fact]
    public async Task GetPending_DoesNotLeakOtherTenantRequests()
    {
        var managerA = BuildPipeline(_tenantA, _managerAUser);
        var managerB = BuildPipeline(_tenantB, _managerBUser);

        var queueA = await managerA.Send(Query());
        var queueB = await managerB.Send(Query());

        queueA.Value!.Items.Should().OnlyContain(i => i.EmployeeId == _reportA1);
        queueB.Value!.Items.Should().OnlyContain(i => i.EmployeeId == _reportB1);
    }
}
