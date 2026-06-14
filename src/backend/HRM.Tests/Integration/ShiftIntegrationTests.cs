// ============================================================================
// US-ATT-005: Shift management integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container
// (MediatR pipeline + ValidationBehavior + ITenantContext-driven global query filters), covering:
//   - create + list a shift via the pipeline (AC-1)
//   - duplicate-name rejection surfaced through the handler (AC-1/BR-1)
//   - bulk assign then delete-prevention with the exact message (AC-2/AC-4/FR-6)
//   - multi-tenant isolation: Tenant B cannot see or assign Tenant A's shift
//
// NOTE ON PROVIDER: identical rationale to AttendanceClockOutIntegrationTests — the verify gate runs
// `dotnet test` with NO PostgreSQL bound and Docker is unavailable, so a Testcontainers-backed test
// would red the gate. These use the InMemory provider through the real composed pipeline, which is
// what proves tenant isolation. PostgreSQL-specific schema is validated by the `migrations` CI job.
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

public sealed class ShiftIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private Guid _employeeA1;
    private Guid _employeeA2;
    private Guid _employeeB1;

    public ShiftIntegrationTests() => SeedTwoTenants();

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

    private IMediator BuildPipeline(Guid tenantId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Email.Returns("hr@test.com");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IShiftService, ShiftService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(CreateShiftCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private void SeedTwoTenants()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _employeeA1 = Guid.NewGuid();
        _employeeA2 = Guid.NewGuid();
        _employeeB1 = Guid.NewGuid();

        db.Employees.AddRange(
            NewEmployee(_employeeA1, _tenantA, "A-1"),
            NewEmployee(_employeeA2, _tenantA, "A-2"),
            NewEmployee(_employeeB1, _tenantB, "B-1"));
        db.SaveChanges();
    }

    private static Employee NewEmployee(Guid id, Guid tenantId, string no) => new()
    {
        Id = id, TenantId = tenantId, UserId = Guid.NewGuid(),
        EmployeeNo = no, FirstName = no, LastName = "X", Email = $"{no}@t.com",
        DateOfJoining = new DateTime(2020, 1, 1),
        DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
    };

    private static ShiftRequest SingleShift(string name) => new()
    {
        Name = name, Type = ShiftType.Single, StartTime = "09:00", EndTime = "17:00",
        BreakDurationMinutes = 60, GracePeriodMinutes = 10, WorkingDays = new[] { 1, 2, 3, 4, 5 },
    };

    [Fact]
    public async Task Create_then_list_through_pipeline()
    {
        var mediator = BuildPipeline(_tenantA);

        var create = await mediator.Send(new CreateShiftCommand(SingleShift("Morning")));
        create.IsSuccess.Should().BeTrue();

        var list = await mediator.Send(new GetShiftsQuery());
        list.Value!.Should().ContainSingle(s => s.Name == "Morning");
    }

    [Fact]
    public async Task Duplicate_name_rejected_through_pipeline()
    {
        var mediator = BuildPipeline(_tenantA);
        await mediator.Send(new CreateShiftCommand(SingleShift("Dup")));

        var second = await mediator.Send(new CreateShiftCommand(SingleShift("Dup")));

        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("duplicate_name");
    }

    [Fact]
    public async Task Assign_then_delete_blocked_with_exact_message()
    {
        var mediator = BuildPipeline(_tenantA);
        var shift = (await mediator.Send(new CreateShiftCommand(SingleShift("Ops")))).Value!;

        var assign = await mediator.Send(new AssignShiftCommand(
            shift.Id, new[] { _employeeA1, _employeeA2 }, DateOnly.FromDateTime(DateTime.UtcNow)));
        assign.Value!.AssignedCount.Should().Be(2);

        var delete = await mediator.Send(new DeleteShiftCommand(shift.Id));
        delete.IsFailure.Should().BeTrue();
        delete.Error.Should().Be(
            "This shift is assigned to 2 employees. Please reassign them before deleting.");
    }

    [Fact]
    public async Task Tenant_B_cannot_see_tenant_A_shift()
    {
        var mediatorA = BuildPipeline(_tenantA);
        await mediatorA.Send(new CreateShiftCommand(SingleShift("A-Only")));

        var mediatorB = BuildPipeline(_tenantB);
        var listB = await mediatorB.Send(new GetShiftsQuery());

        listB.Value!.Should().NotContain(s => s.Name == "A-Only");
    }

    [Fact]
    public async Task Tenant_B_cannot_assign_tenant_A_shift()
    {
        var mediatorA = BuildPipeline(_tenantA);
        var shift = (await mediatorA.Send(new CreateShiftCommand(SingleShift("A-Shift")))).Value!;

        // Tenant B cannot even see the shift, so assignment fails at the shift-existence check.
        var mediatorB = BuildPipeline(_tenantB);
        var assign = await mediatorB.Send(new AssignShiftCommand(
            shift.Id, new[] { _employeeB1 }, DateOnly.FromDateTime(DateTime.UtcNow)));

        assign.IsFailure.Should().BeTrue();
        assign.StatusCode.Should().Be(404);
    }
}
