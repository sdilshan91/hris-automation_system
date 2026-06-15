// ============================================================================
// US-PAY-002: Salary assignment — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (MediatR pipeline +
// ITenantContext-driven global query filters), covering:
//   - AC-1: assigning a structure creates employee_salary_component rows with correct tenant_id.
//   - AC-5: cross-tenant isolation — Tenant B cannot read Tenant A's compensation/history.
//   - FR-7: assigning an inactive structure returns 400.
//   - AC-4/FR-5: bulk assign creates rows for every valid employee under the calling tenant.
//
// NOTE ON PROVIDER: identical rationale to the US-PAY-001 / Vacancy / Attendance integration tests — the
// verify gate runs `dotnet test` with NO PostgreSQL bound and Docker is unavailable in the agent sandbox,
// so a Testcontainers test would red the gate. These tests use the InMemory provider through the real
// composed pipeline (MediatR + tenant query filters), which is what proves tenant isolation (AC-5).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Commands;
using HRM.Application.Features.Payroll.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration;

public sealed class SalaryAssignmentIntegrationTests
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

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; init; } = Guid.NewGuid();
        public string Email => "hr@t.com";
        public Guid TenantId { get; init; }
        public Guid UserTenantId => TenantId;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => true;
    }

    private IServiceProvider Provider(Guid tenantId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = tenantId });
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<ISalaryComponentService, SalaryComponentService>();
        services.AddScoped<ISalaryStructureService, SalaryStructureService>();
        services.AddScoped<ISalaryAssignmentService, SalaryAssignmentService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateSalaryComponentCommand).Assembly));
        return services.BuildServiceProvider();
    }

    private IMediator Pipeline(Guid tenantId) => Provider(tenantId).GetRequiredService<IMediator>();

    private async Task<Guid> SeedEmployee(Guid tenantId, string no)
    {
        var provider = Provider(tenantId);
        var db = provider.GetRequiredService<AppDbContext>();
        var employee = new Employee
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            EmployeeNo = no,
            FirstName = "Test",
            LastName = no,
            Email = $"{no.ToLower()}@t.com",
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private async Task<Guid> SeedStructure(Guid tenantId, string code = "FT", bool isActive = true)
    {
        var mediator = Pipeline(tenantId);
        // Basic 100% of CTC → single earning that equals the declared CTC exactly (FR-6 passes).
        var basic = await mediator.Send(new CreateSalaryComponentCommand(
            "BASIC", "BASIC", SalaryComponentType.Earning, CalculationMethod.PercentageOfGross, 100m, null, true, false, true, 1));
        basic.IsSuccess.Should().BeTrue();

        var structure = await mediator.Send(new CreateSalaryStructureCommand(
            "Structure " + code, code, null, new DateOnly(2026, 1, 1), false, isActive,
            new[] { new SalaryStructureComponentInput(basic.Value!.Id, null, null, 1, false) }));
        structure.IsSuccess.Should().BeTrue();
        return structure.Value!.Id;
    }

    // ── AC-1: assignment creates rows with correct tenant_id ──────────

    [Fact]
    public async Task Assign_CreatesEmployeeSalaryComponentRows_WithTenantId()
    {
        var mediator = Pipeline(_tenantA);
        var employee = await SeedEmployee(_tenantA, "EMP-1");
        var structureId = await SeedStructure(_tenantA);

        var assign = await mediator.Send(new AssignSalaryStructureCommand(
            employee, structureId, new DateOnly(2026, 1, 1), 600_000m, "Initial", Array.Empty<SalaryOverrideInput>()));
        assign.IsSuccess.Should().BeTrue();

        // Verify rows persisted under tenant A (IgnoreQueryFilters to read raw tenant_id).
        var db = Provider(_tenantA).GetRequiredService<AppDbContext>();
        var rows = await db.EmployeeSalaryComponents.IgnoreQueryFilters()
            .Where(r => r.EmployeeId == employee).ToListAsync();
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r => r.TenantId == _tenantA);
    }

    // ── AC-5: cross-tenant isolation ──────────────────────────────────

    [Fact]
    public async Task TenantB_CannotSeeTenantACompensation()
    {
        var a = Pipeline(_tenantA);
        var employee = await SeedEmployee(_tenantA, "EMP-A");
        var structureId = await SeedStructure(_tenantA);
        await a.Send(new AssignSalaryStructureCommand(
            employee, structureId, DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1),
            600_000m, null, Array.Empty<SalaryOverrideInput>()));

        var b = Pipeline(_tenantB);
        var comp = await b.Send(new GetEmployeeCompensationQuery(employee));
        comp.IsFailure.Should().BeTrue();
        comp.StatusCode.Should().Be(404);

        var history = await b.Send(new GetSalaryRevisionHistoryQuery(employee));
        history.Value!.Should().BeEmpty();
    }

    // ── FR-7: inactive structure → 400 ────────────────────────────────

    [Fact]
    public async Task Assign_InactiveStructure_Returns400()
    {
        var mediator = Pipeline(_tenantA);
        var employee = await SeedEmployee(_tenantA, "EMP-2");
        var structureId = await SeedStructure(_tenantA, "INACT", isActive: false);

        var assign = await mediator.Send(new AssignSalaryStructureCommand(
            employee, structureId, new DateOnly(2026, 1, 1), 600_000m, null, Array.Empty<SalaryOverrideInput>()));

        assign.IsFailure.Should().BeTrue();
        assign.StatusCode.Should().Be(400);
        assign.ErrorCode.Should().Be("structure_inactive");
    }

    // ── AC-4 / FR-5: bulk assign ──────────────────────────────────────

    [Fact]
    public async Task BulkAssign_CreatesRows_ForAllEmployees_WithCorrectTenantId()
    {
        var mediator = Pipeline(_tenantA);
        var structureId = await SeedStructure(_tenantA);

        var e1 = await SeedEmployee(_tenantA, "BULK-1");
        var e2 = await SeedEmployee(_tenantA, "BULK-2");
        var e3 = await SeedEmployee(_tenantA, "BULK-3");

        var entries = new[]
        {
            new BulkAssignEntryInput(e1, 500_000m, Array.Empty<SalaryOverrideInput>()),
            new BulkAssignEntryInput(e2, 600_000m, Array.Empty<SalaryOverrideInput>()),
            new BulkAssignEntryInput(e3, 700_000m, Array.Empty<SalaryOverrideInput>()),
        };

        var result = await mediator.Send(new BulkAssignSalaryStructureCommand(
            structureId, new DateOnly(2026, 1, 1), "Bulk onboarding", entries));

        result.IsSuccess.Should().BeTrue();
        result.Value!.SucceededCount.Should().Be(3);
        result.Value.FailedCount.Should().Be(0);

        var db = Provider(_tenantA).GetRequiredService<AppDbContext>();
        var allRows = await db.EmployeeSalaryComponents.IgnoreQueryFilters().ToListAsync();
        allRows.Should().OnlyContain(r => r.TenantId == _tenantA);
        allRows.Select(r => r.EmployeeId).Distinct().Should().BeEquivalentTo(new[] { e1, e2, e3 });

        // Each employee's CTC was applied individually.
        var e2Comp = await mediator.Send(new GetEmployeeCompensationQuery(e2));
        e2Comp.Value!.AnnualCtc.Should().Be(600_000m);
    }

    [Fact]
    public async Task BulkAssign_SkipsUnknownEmployee_ButSucceedsForValidOnes()
    {
        var mediator = Pipeline(_tenantA);
        var structureId = await SeedStructure(_tenantA);
        var valid = await SeedEmployee(_tenantA, "VALID-1");
        var bogus = Guid.NewGuid();

        var result = await mediator.Send(new BulkAssignSalaryStructureCommand(
            structureId, new DateOnly(2026, 1, 1), null, new[]
            {
                new BulkAssignEntryInput(valid, 500_000m, Array.Empty<SalaryOverrideInput>()),
                new BulkAssignEntryInput(bogus, 500_000m, Array.Empty<SalaryOverrideInput>()),
            }));

        result.IsSuccess.Should().BeTrue();
        result.Value!.SucceededCount.Should().Be(1);
        result.Value.FailedCount.Should().Be(1);
        result.Value.Results.Single(r => r.EmployeeId == bogus).ErrorCode.Should().Be("employee_not_found");
    }
}
