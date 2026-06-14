// ============================================================================
// US-ATT-004: Manager approve/reject of attendance regularization — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (MediatR
// pipeline + ITenantContext-driven global query filters), covering:
//   - ApproveRegularizationCommand end-to-end: status APPROVED + attendance_log created/recalculated
//     + immutable history row (AC-1/FR-2/FR-6)
//   - RejectRegularizationCommand end-to-end (AC-2/FR-3)
//   - the manager pending-queue query (FR-1/AC-3)
//   - bulk approve (BR-7)
//   - Tenant isolation: a manager in Tenant A cannot see or act on a Tenant B regularization (NFR-3).
//
// NOTE ON PROVIDER: identical rationale to AttendanceRegularizationIntegrationTests — the verify gate
// runs `dotnet test` with NO PostgreSQL bound and Docker is unavailable in the agent sandbox, so a
// Testcontainers test would red the gate. These tests use the InMemory provider through the real
// composed pipeline (MediatR + tenant query filters), which is what proves tenant isolation.
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

public sealed class RegularizationApprovalIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _managerUserA = Guid.NewGuid();
    private readonly Guid _managerUserB = Guid.NewGuid();

    private Guid _managerA, _reportA;
    private Guid _managerB, _reportB;

    public RegularizationApprovalIntegrationTests()
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
        services.AddScoped<IRegularizationApprovalService, RegularizationApprovalService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ApproveRegularizationCommand).Assembly));

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IMediator>(), provider);
    }

    private static DateOnly Yesterday => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
    private static DateTime At(DateOnly date, int hour) => date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc);

    private void SeedTwoTenants()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _managerA = Guid.NewGuid(); _reportA = Guid.NewGuid();
        _managerB = Guid.NewGuid(); _reportB = Guid.NewGuid();

        db.Employees.AddRange(
            Emp(_managerA, _tenantA, _managerUserA, "MgrA", null),
            Emp(_reportA, _tenantA, Guid.NewGuid(), "RepA", _managerA),
            Emp(_managerB, _tenantB, _managerUserB, "MgrB", null),
            Emp(_reportB, _tenantB, Guid.NewGuid(), "RepB", _managerB));
        db.SaveChanges();
    }

    private static Employee Emp(Guid id, Guid tenantId, Guid userId, string name, Guid? reportsTo) => new()
    {
        Id = id, TenantId = tenantId, UserId = userId,
        EmployeeNo = name, FirstName = name, LastName = "X", Email = $"{name}@t.com",
        DateOfJoining = new DateTime(2020, 1, 1),
        DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        ReportsToEmployeeId = reportsTo,
    };

    private Guid SeedPending(Guid tenantId, Guid employeeId, DateOnly date)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        var id = BaseEntity.NewUuidV7();
        db.AttendanceRegularizations.Add(new AttendanceRegularization
        {
            Id = id, TenantId = tenantId, EmployeeId = employeeId, Date = date,
            RegularizationType = RegularizationType.MissedBoth,
            RequestedClockIn = At(date, 9), RequestedClockOut = At(date, 17),
            Reason = "Forgot to clock in and out on this day",
            Status = RegularizationStatus.Pending,
        });
        db.SaveChanges();
        return id;
    }

    // ── AC-1 / FR-2 / FR-6: approve end-to-end ──────────────────────────────

    [Fact]
    public async Task Approve_HappyPath_UpdatesLog_AndWritesHistory()
    {
        var date = Yesterday;
        var regId = SeedPending(_tenantA, _reportA, date);
        var (mediator, provider) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediator.Send(new ApproveRegularizationCommand(regId, "ok"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(RegularizationStatus.Approved);
        result.Value.AttendanceLogId.Should().NotBeNull();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AttendanceLogs.Should().ContainSingle(a => a.EmployeeId == _reportA);
        db.RegularizationApprovalHistories.Should().ContainSingle(h =>
            h.RegularizationId == regId && h.Action == RegularizationApprovalAction.Approved);
    }

    // ── AC-2 / FR-3: reject end-to-end ──────────────────────────────────────

    [Fact]
    public async Task Reject_HappyPath_SetsRejected()
    {
        var regId = SeedPending(_tenantA, _reportA, Yesterday);
        var (mediator, provider) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediator.Send(
            new RejectRegularizationCommand(regId, "The requested times conflict with badge data"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(RegularizationStatus.Rejected);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AttendanceLogs.Should().BeEmpty();
    }

    // ── FR-1 / AC-3: pending queue ──────────────────────────────────────────

    [Fact]
    public async Task GetPending_ReturnsDirectReportRequest()
    {
        SeedPending(_tenantA, _reportA, Yesterday);
        var (mediator, _) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediator.Send(
            new GetPendingRegularizationsQuery(new PendingRegularizationQueryParams()));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle(i => i.EmployeeId == _reportA);
    }

    // ── BR-7: bulk approve ──────────────────────────────────────────────────

    [Fact]
    public async Task BulkApprove_ApprovesMultiple()
    {
        var r1 = SeedPending(_tenantA, _reportA, Yesterday);
        var r2 = SeedPending(_tenantA, _reportA, Yesterday.AddDays(-1));
        var (mediator, provider) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediator.Send(
            new BulkApproveRegularizationsCommand(new[] { r1, r2 }, null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.SucceededCount.Should().Be(2);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AttendanceRegularizations
            .Count(r => r.Status == RegularizationStatus.Approved).Should().Be(2);
    }

    // ── NFR-3: tenant isolation — Tenant A manager cannot act on a Tenant B request ──

    [Fact]
    public async Task Approve_CrossTenantRegularization_IsNotFound()
    {
        var regB = SeedPending(_tenantB, _reportB, Yesterday);
        var (mediatorA, _) = BuildPipeline(_tenantA, _managerUserA);

        var result = await mediatorA.Send(new ApproveRegularizationCommand(regB, null));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);   // invisible across tenants — never leaks existence.
    }

    [Fact]
    public async Task GetPending_DoesNotLeakOtherTenantRequests()
    {
        SeedPending(_tenantA, _reportA, Yesterday);
        SeedPending(_tenantB, _reportB, Yesterday);

        var (mediatorB, _) = BuildPipeline(_tenantB, _managerUserB);
        var result = await mediatorB.Send(
            new GetPendingRegularizationsQuery(new PendingRegularizationQueryParams()));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle(i => i.EmployeeId == _reportB);
        result.Value.Items.Should().NotContain(i => i.EmployeeId == _reportA);
    }
}
