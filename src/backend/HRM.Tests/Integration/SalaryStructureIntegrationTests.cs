// ============================================================================
// US-PAY-001: Salary component + structure configuration — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (MediatR
// pipeline + ITenantContext-driven global query filters), covering:
//   - Component create -> list happy path (AC-1, FR-1).
//   - BR-2: duplicate component code per tenant is rejected (and two tenants may share a code).
//   - AC-5: deleting a component used by a structure returns 409 Conflict.
//   - AC-6: cross-tenant isolation — Tenant B cannot see Tenant A's components/structures.
//   - FR-5: an active structure requires at least one earning component.
//   - BR-1: only one default structure per tenant.
//
// NOTE ON PROVIDER: identical rationale to the Vacancy/Attendance integration tests — the verify gate
// runs `dotnet test` with NO PostgreSQL bound and Docker is unavailable in the agent sandbox, so a
// Testcontainers test would red the gate. These tests use the InMemory provider through the real
// composed pipeline (MediatR + tenant query filters), which is what proves tenant isolation (AC-6).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Commands;
using HRM.Application.Features.Payroll.Queries;
using HRM.Domain.Enums;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration;

public sealed class SalaryStructureIntegrationTests
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

    private IMediator Pipeline(Guid tenantId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = tenantId });
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IPayrollAuditLogger, PayrollAuditLogger>();
        services.AddScoped<ISalaryComponentService, SalaryComponentService>();
        services.AddScoped<ISalaryStructureService, SalaryStructureService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateSalaryComponentCommand).Assembly));
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static CreateSalaryComponentCommand Earning(string code = "BASIC")
        => new(code, code, SalaryComponentType.Earning, CalculationMethod.Fixed, 1000m, null, true, false, true, 1);

    // ── AC-1 / FR-1: create + list ────────────────────────────────────

    [Fact]
    public async Task CreateComponent_AppearsInList()
    {
        var mediator = Pipeline(_tenantA);
        var created = await mediator.Send(Earning());
        created.IsSuccess.Should().BeTrue();

        var list = await mediator.Send(new ListSalaryComponentsQuery(null, null, null));
        list.Value!.TotalCount.Should().Be(1);
        list.Value.Items.Single().Code.Should().Be("BASIC");
    }

    // ── BR-2: unique code ─────────────────────────────────────────────

    [Fact]
    public async Task DuplicateComponentCode_SameTenant_IsRejected()
    {
        var mediator = Pipeline(_tenantA);
        await mediator.Send(Earning());

        var dup = await mediator.Send(Earning());
        dup.IsFailure.Should().BeTrue();
        dup.ErrorCode.Should().Be("duplicate_code");
    }

    [Fact]
    public async Task SameComponentCode_DifferentTenants_IsAllowed()
    {
        (await Pipeline(_tenantA).Send(Earning())).IsSuccess.Should().BeTrue();
        (await Pipeline(_tenantB).Send(Earning())).IsSuccess.Should().BeTrue();
    }

    // ── AC-5: delete-conflict when in use ─────────────────────────────

    [Fact]
    public async Task DeleteComponent_InUseByStructure_Returns409()
    {
        var mediator = Pipeline(_tenantA);
        var component = await mediator.Send(Earning());

        var structure = await mediator.Send(new CreateSalaryStructureCommand(
            "Full-Time", "FT", null, new DateOnly(2026, 1, 1), false, true,
            new[] { new SalaryStructureComponentInput(component.Value!.Id, null, null, 1, false) }));
        structure.IsSuccess.Should().BeTrue();

        var del = await mediator.Send(new DeleteSalaryComponentCommand(component.Value.Id));
        del.IsFailure.Should().BeTrue();
        del.StatusCode.Should().Be(409);
        del.ErrorCode.Should().Be("component_in_use");
    }

    // ── AC-6: cross-tenant isolation ──────────────────────────────────

    [Fact]
    public async Task TenantB_CannotSeeTenantAComponents()
    {
        var a = Pipeline(_tenantA);
        var created = await a.Send(Earning("APAY"));

        var b = Pipeline(_tenantB);
        var listB = await b.Send(new ListSalaryComponentsQuery(null, null, null));
        listB.Value!.TotalCount.Should().Be(0);

        var getB = await b.Send(new GetSalaryComponentByIdQuery(created.Value!.Id));
        getB.IsFailure.Should().BeTrue();
        getB.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task TenantB_CannotSeeTenantAStructures()
    {
        var a = Pipeline(_tenantA);
        var basic = await a.Send(Earning());
        var structure = await a.Send(new CreateSalaryStructureCommand(
            "FT", "FT", null, new DateOnly(2026, 1, 1), false, true,
            new[] { new SalaryStructureComponentInput(basic.Value!.Id, null, null, 1, false) }));

        var b = Pipeline(_tenantB);
        var getB = await b.Send(new GetSalaryStructureByIdQuery(structure.Value!.Id));
        getB.IsFailure.Should().BeTrue();
        getB.StatusCode.Should().Be(404);
    }

    // ── FR-5 + BR-1 through the pipeline ──────────────────────────────

    [Fact]
    public async Task ActiveStructure_WithoutEarning_IsRejected()
    {
        var mediator = Pipeline(_tenantA);
        var deduction = await mediator.Send(new CreateSalaryComponentCommand(
            "LOAN", "LOAN", SalaryComponentType.Deduction, CalculationMethod.Fixed, 100m, null, false, false, true, 1));

        var structure = await mediator.Send(new CreateSalaryStructureCommand(
            "Bad", "BAD", null, new DateOnly(2026, 1, 1), false, true,
            new[] { new SalaryStructureComponentInput(deduction.Value!.Id, null, null, 1, false) }));

        structure.IsFailure.Should().BeTrue();
        structure.ErrorCode.Should().Be("no_earning_component");
    }

    [Fact]
    public async Task OnlyOneDefaultStructure_PerTenant()
    {
        var mediator = Pipeline(_tenantA);
        var basic = await mediator.Send(Earning());
        var comp = new[] { new SalaryStructureComponentInput(basic.Value!.Id, null, null, 1, false) };

        var d1 = await mediator.Send(new CreateSalaryStructureCommand("D1", "D1", null, new DateOnly(2026, 1, 1), true, true, comp));
        var d2 = await mediator.Send(new CreateSalaryStructureCommand("D2", "D2", null, new DateOnly(2026, 1, 1), true, true, comp));
        d1.IsSuccess.Should().BeTrue();
        d2.IsSuccess.Should().BeTrue();

        var d1Reloaded = await mediator.Send(new GetSalaryStructureByIdQuery(d1.Value!.Id));
        d1Reloaded.Value!.IsDefault.Should().BeFalse();
        var d2Reloaded = await mediator.Send(new GetSalaryStructureByIdQuery(d2.Value!.Id));
        d2Reloaded.Value!.IsDefault.Should().BeTrue();
    }
}
