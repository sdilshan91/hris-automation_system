// ============================================================================
// US-PRF-004: HR appraisal cycle management — integration tests.
//
// Exercises AppraisalCycleService over a real AppDbContext (InMemory) with the ITenantContext-driven
// global query filter, covering the cross-cutting concerns a service unit test cannot:
//   - NFR-2: tenant isolation — a cycle created in Tenant A is invisible to Tenant B; a Tenant A service
//            cannot read/transition a Tenant B cycle (the global filter hides it ⇒ cycle_not_found).
//   - FR-3: department scoping rejects a foreign-department employee (a department-scoped cycle resolves
//           ONLY that department's employees; a custom list with a foreign-dept id outside the tenant fails).
//   - BR-4: an employee cannot be in two ACTIVE cycles of the same type (annual + annual conflict; annual +
//           quarterly allowed); the conflict is scoped within a tenant (Tenant B's active cycle never blocks A).
//   - persistence: phases + participants round-trip; soft-delete removes a draft cycle from tenant reads.
//
// PROVIDER: InMemory — same rationale as the other integration tests here (the verify gate runs
// `dotnet test` with no PostgreSQL / Docker). Real-PG RLS is validated by the `migrations` CI job.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class AppraisalCycleIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

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

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private AppraisalCycleService Service(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        var db = new AppDbContext(options, ctx);

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(Guid.NewGuid());
        user.IsAuthenticated.Returns(true);
        user.Email.Returns("hr@test.com");

        return new AppraisalCycleService(db, ctx, user,
            Substitute.For<IPerformanceNotificationService>(),
            NullLogger<AppraisalCycleService>.Instance);
    }

    /// <summary>Seeds two active employees (HR + Eng departments) for a tenant; returns their ids.</summary>
    private async Task<(Guid EmpHr, Guid EmpEng, Guid DeptHr, Guid DeptEng)> SeedEmployeesAsync(Guid tenantId)
    {
        using var db = Db(tenantId);
        var empHr = Guid.NewGuid();
        var empEng = Guid.NewGuid();
        var deptHr = Guid.NewGuid();
        var deptEng = Guid.NewGuid();

        db.Employees.Add(new Employee
        {
            Id = empHr, TenantId = tenantId, EmployeeNo = "HR-1", FirstName = "Ada", LastName = "L",
            Email = "ada@t.com", Status = EmployeeStatus.Active, DepartmentId = deptHr,
        });
        db.Employees.Add(new Employee
        {
            Id = empEng, TenantId = tenantId, EmployeeNo = "ENG-1", FirstName = "Alan", LastName = "T",
            Email = "alan@t.com", Status = EmployeeStatus.Active, DepartmentId = deptEng,
        });
        await db.SaveChangesAsync();
        return (empHr, empEng, deptHr, deptEng);
    }

    private static IReadOnlyList<CyclePhaseInput> Phases(DateTime start) =>
    [
        new(CyclePhaseType.GoalSetting, start, start.AddDays(10)),
        new(CyclePhaseType.SelfAssessment, start.AddDays(11), start.AddDays(20)),
        new(CyclePhaseType.ManagerReview, start.AddDays(21), start.AddDays(30)),
    ];

    private static CreateCycleInput Input(
        DateTime start, ParticipantScopeInput scope, CycleType type = CycleType.Annual, string name = "Cycle")
        => new(name, type, start, start.AddDays(40), Phases(start), scope, 5, 30, false, false, false);

    private static ParticipantScopeInput AllScope() => new(ParticipantScopeType.AllEmployees);

    // ── NFR-2: tenant isolation ─────────────────────────────────────────

    [Fact]
    public async Task Cycle_CreatedInTenantA_IsInvisibleToTenantB()
    {
        await SeedEmployeesAsync(_tenantA);
        var start = DateTime.UtcNow.Date.AddDays(1);

        var created = await Service(_tenantA).CreateAsync(Input(start, AllScope(), name: "A-Cycle"));
        created.IsSuccess.Should().BeTrue(created.Error);

        // Tenant A sees it; Tenant B sees nothing.
        (await Service(_tenantA).ListAsync(null)).Value!.Should().ContainSingle();
        (await Service(_tenantB).ListAsync(null)).Value!.Should().BeEmpty();

        using var dbB = Db(_tenantB);
        (await dbB.AppraisalCycles.AsNoTracking().CountAsync()).Should().Be(0);
        (await dbB.CyclePhases.AsNoTracking().CountAsync()).Should().Be(0);
        (await dbB.CycleParticipants.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ServiceInTenantA_CannotRead_TenantBCycle()
    {
        await SeedEmployeesAsync(_tenantB);
        var start = DateTime.UtcNow.Date.AddDays(1);
        var created = await Service(_tenantB).CreateAsync(Input(start, AllScope(), name: "B-Cycle"));
        created.IsSuccess.Should().BeTrue(created.Error);

        // Tenant A asks for B's cycle by id — the global filter hides it ⇒ not found (no cross-tenant read).
        var read = await Service(_tenantA).GetByIdAsync(created.Value!.Id);

        read.IsFailure.Should().BeTrue();
        read.StatusCode.Should().Be(404);
        read.ErrorCode.Should().Be("cycle_not_found");
    }

    [Fact]
    public async Task ServiceInTenantA_CannotTransition_TenantBCycle()
    {
        await SeedEmployeesAsync(_tenantB);
        var start = DateTime.UtcNow.Date.AddDays(1);
        var created = await Service(_tenantB).CreateAsync(Input(start, AllScope(), name: "B-Cycle"));
        created.IsSuccess.Should().BeTrue(created.Error);

        // A cross-tenant write must be blocked at the lookup (cycle_not_found), never mutate B's row.
        var transition = await Service(_tenantA).TransitionStatusAsync(
            created.Value!.Id, AppraisalCycleStatus.Active, null);

        transition.IsFailure.Should().BeTrue();
        transition.ErrorCode.Should().Be("cycle_not_found");

        // B's cycle is untouched (still Draft).
        (await Service(_tenantB).GetByIdAsync(created.Value!.Id)).Value!.Status
            .Should().Be(AppraisalCycleStatus.Draft);
    }

    // ── FR-3: participant scoping ───────────────────────────────────────

    [Fact]
    public async Task DepartmentScope_RejectsForeignDepartmentEmployee()
    {
        var (_, _, _, deptEng) = await SeedEmployeesAsync(_tenantA);
        var start = DateTime.UtcNow.Date.AddDays(1);

        // Scope to Eng only ⇒ the HR employee (foreign department) must NOT be enrolled.
        var scope = new ParticipantScopeInput(ParticipantScopeType.Departments, DepartmentIds: new[] { deptEng });
        var created = await Service(_tenantA).CreateAsync(Input(start, scope, name: "Eng-Cycle"));

        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value!.ParticipantCount.Should().Be(1); // only the Eng employee

        using var db = Db(_tenantA);
        var participants = await db.CycleParticipants.AsNoTracking()
            .Where(p => p.CycleId == created.Value!.Id).ToListAsync();
        participants.Should().ContainSingle();
    }

    [Fact]
    public async Task CustomListScope_WithEmployeeFromAnotherTenant_IsRejected()
    {
        var a = await SeedEmployeesAsync(_tenantA);
        var b = await SeedEmployeesAsync(_tenantB);
        var start = DateTime.UtcNow.Date.AddDays(1);

        // Tenant A tries to enroll one of its own + one of Tenant B's employees.
        var scope = new ParticipantScopeInput(
            ParticipantScopeType.CustomList, EmployeeIds: new[] { a.EmpHr, b.EmpEng });
        var created = await Service(_tenantA).CreateAsync(Input(start, scope, name: "X"));

        // B's employee is invisible under A's filter ⇒ rejected as an invalid participant, cycle not created.
        created.IsFailure.Should().BeTrue();
        created.ErrorCode.Should().Be("invalid_participant");

        using var dbA = Db(_tenantA);
        (await dbA.AppraisalCycles.AsNoTracking().CountAsync()).Should().Be(0);
    }

    // ── BR-4: no two active same-type cycles per employee ───────────────

    [Fact]
    public async Task EmployeeCannotBeInTwoActiveCyclesOfTheSameType()
    {
        var a = await SeedEmployeesAsync(_tenantA);
        var start = DateTime.UtcNow.Date.AddDays(1);
        var scope = new ParticipantScopeInput(ParticipantScopeType.CustomList, EmployeeIds: new[] { a.EmpHr });

        var first = await Service(_tenantA).CreateAsync(Input(start, scope, CycleType.Annual, "Annual-1"));
        first.IsSuccess.Should().BeTrue(first.Error);
        (await Service(_tenantA).TransitionStatusAsync(first.Value!.Id, AppraisalCycleStatus.Active, null))
            .IsSuccess.Should().BeTrue();

        var second = await Service(_tenantA).CreateAsync(Input(start, scope, CycleType.Annual, "Annual-2"));

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("active_same_type_conflict");
    }

    [Fact]
    public async Task EmployeeCanBeInActiveCyclesOfDifferentTypes()
    {
        var a = await SeedEmployeesAsync(_tenantA);
        var start = DateTime.UtcNow.Date.AddDays(1);
        var scope = new ParticipantScopeInput(ParticipantScopeType.CustomList, EmployeeIds: new[] { a.EmpHr });

        var annual = await Service(_tenantA).CreateAsync(Input(start, scope, CycleType.Annual, "Annual"));
        annual.IsSuccess.Should().BeTrue(annual.Error);
        (await Service(_tenantA).TransitionStatusAsync(annual.Value!.Id, AppraisalCycleStatus.Active, null))
            .IsSuccess.Should().BeTrue();

        var quarterly = await Service(_tenantA).CreateAsync(Input(start, scope, CycleType.Quarterly, "Quarterly"));

        quarterly.IsSuccess.Should().BeTrue(quarterly.Error);
    }

    [Fact]
    public async Task BR4_Conflict_IsScopedWithinTenant()
    {
        // Same employee-id value is irrelevant cross-tenant; ensure B's active cycle never blocks A.
        var a = await SeedEmployeesAsync(_tenantA);
        var b = await SeedEmployeesAsync(_tenantB);
        var start = DateTime.UtcNow.Date.AddDays(1);

        var bScope = new ParticipantScopeInput(ParticipantScopeType.CustomList, EmployeeIds: new[] { b.EmpHr });
        var bCycle = await Service(_tenantB).CreateAsync(Input(start, bScope, CycleType.Annual, "B-Annual"));
        bCycle.IsSuccess.Should().BeTrue(bCycle.Error);
        (await Service(_tenantB).TransitionStatusAsync(bCycle.Value!.Id, AppraisalCycleStatus.Active, null))
            .IsSuccess.Should().BeTrue();

        // Tenant A creates an annual cycle for its own employees — must succeed despite B having an active annual.
        var aScope = new ParticipantScopeInput(ParticipantScopeType.CustomList, EmployeeIds: new[] { a.EmpHr });
        var aCycle = await Service(_tenantA).CreateAsync(Input(start, aScope, CycleType.Annual, "A-Annual"));

        aCycle.IsSuccess.Should().BeTrue(aCycle.Error);
    }

    // ── Persistence: round-trip + soft-delete ───────────────────────────

    [Fact]
    public async Task PhasesAndParticipants_RoundTrip()
    {
        await SeedEmployeesAsync(_tenantA);
        var start = DateTime.UtcNow.Date.AddDays(1);
        var created = await Service(_tenantA).CreateAsync(Input(start, AllScope(), name: "RT"));
        created.IsSuccess.Should().BeTrue(created.Error);

        var read = await Service(_tenantA).GetByIdAsync(created.Value!.Id);
        read.IsSuccess.Should().BeTrue();
        read.Value!.Phases.Should().HaveCount(3);
        read.Value!.Phases.Select(p => p.PhaseType).Should()
            .Equal(CyclePhaseType.GoalSetting, CyclePhaseType.SelfAssessment, CyclePhaseType.ManagerReview);
        read.Value!.ParticipantCount.Should().Be(2);
    }

    [Fact]
    public async Task DeletedDraftCycle_IsRemovedFromTenantReads()
    {
        await SeedEmployeesAsync(_tenantA);
        var start = DateTime.UtcNow.Date.AddDays(1);
        var created = await Service(_tenantA).CreateAsync(Input(start, AllScope(), name: "ToDelete"));
        created.IsSuccess.Should().BeTrue(created.Error);

        (await Service(_tenantA).DeleteAsync(created.Value!.Id)).IsSuccess.Should().BeTrue();

        (await Service(_tenantA).ListAsync(null)).Value!.Should().BeEmpty();
        (await Service(_tenantA).GetByIdAsync(created.Value!.Id)).ErrorCode.Should().Be("cycle_not_found");
    }
}
