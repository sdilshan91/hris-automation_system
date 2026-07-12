// ============================================================================
// US-PAY-006: Statutory deduction configuration — integration tests.
//
// Exercises the full handler -> service/resolver -> DbContext path through a real DI container (MediatR
// pipeline + ITenantContext-driven global query filters), covering:
//   - AC-4: tenant isolation — one tenant's statutory rules are invisible from another tenant.
//   - AC-3 + FR-4: create FY2026-2027 income-tax slabs, resolve them for a period in that FY (test calc).
//   - AC-3: clone a fiscal year's rules into a new fiscal year.
//   - FR-5: test calculation computes tax + EPF without persisting.
//   - FR-6: contiguity is enforced (gap rejected) through the validation pipeline.
//   - AC-5: a mandatory statutory component cannot be dropped from a structure.
//
// PROVIDER: InMemory through the real composed pipeline — same rationale as the other Payroll/Attendance
// integration tests (the verify gate runs dotnet test with no PostgreSQL / Docker).
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

public sealed class StatutoryRuleIntegrationTests
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
        public bool IsImpersonating => false;
        public Guid? ImpersonatorId => null;
        public Guid? ImpersonationSessionId => null;
        public bool ImpersonationReadOnly => false;
    }

    private IServiceProvider Provider(Guid tenantId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = tenantId });
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IPayrollAuditLogger, PayrollAuditLogger>();
        services.AddScoped<IStatutoryDeductionResolver, StatutoryDeductionResolver>();
        services.AddScoped<IStatutoryRuleService, StatutoryRuleService>();
        services.AddScoped<ISalaryComponentService, SalaryComponentService>();
        services.AddScoped<ISalaryStructureService, SalaryStructureService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateStatutoryRuleCommand).Assembly));
        return services.BuildServiceProvider();
    }

    private IMediator Pipeline(Guid tenantId) => Provider(tenantId).GetRequiredService<IMediator>();

    private static CreateStatutoryRuleCommand IncomeTaxCommand(string fiscalYear) => new(
        StatutoryRuleType.IncomeTax, "PAYE", "LK", fiscalYear,
        new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true,
        new List<TaxSlabInput>
        {
            new(0m, 250_000m, 0m, 0),
            new(250_000m, 500_000m, 5m, 1),
            new(500_000m, 1_000_000m, 20m, 2),
            new(1_000_000m, null, 30m, 3),
        },
        null);

    private static CreateStatutoryRuleCommand EpfCommand(string fiscalYear) => new(
        StatutoryRuleType.EPF, "EPF", "LK", fiscalYear,
        new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true,
        new List<TaxSlabInput>(),
        new SocialSecurityInputDto(12m, 12m, 180_000m, StatutoryApplicableOn.Basic, null));

    // ── AC-4: tenant isolation ───────────────────────────────────────────────
    [Fact]
    public async Task StatutoryRules_AreInvisibleAcrossTenants()
    {
        var created = await Pipeline(_tenantA).Send(IncomeTaxCommand("2026-2027"));
        created.IsSuccess.Should().BeTrue();

        var fromB = await Pipeline(_tenantB).Send(new GetStatutoryRuleByIdQuery(created.Value!.Id));
        fromB.IsFailure.Should().BeTrue();
        fromB.StatusCode.Should().Be(404);

        var listB = await Pipeline(_tenantB).Send(new ListStatutoryRulesQuery(null, null, null, 1, 25));
        listB.Value!.TotalCount.Should().Be(0);

        var listA = await Pipeline(_tenantA).Send(new ListStatutoryRulesQuery(null, null, null, 1, 25));
        listA.Value!.TotalCount.Should().Be(1);
    }

    // ── AC-3 + FR-4 + FR-5: create FY slabs, resolve for a period in that FY ──
    [Fact]
    public async Task CreateFiscalYearSlabs_ThenResolveForPeriod_AppliesCorrectTax()
    {
        var mediator = Pipeline(_tenantA);
        (await mediator.Send(IncomeTaxCommand("2026-2027"))).IsSuccess.Should().BeTrue();
        (await mediator.Send(EpfCommand("2026-2027"))).IsSuccess.Should().BeTrue();

        var calc = await mediator.Send(new TestStatutoryCalculationQuery(
            MonthlyGross: 750_000m, MonthlyBasic: 20_000m, DeclaredMonthlyExemptions: 0m, ExemptEarnings: 0m, FiscalYear: "2026-2027"));

        calc.IsSuccess.Should().BeTrue();
        calc.Value!.IncomeTax.Should().Be(62_500m);
        calc.Value.EmployeeEpf.Should().Be(1_800m);
        calc.Value.EmployerEpf.Should().Be(1_800m);
        calc.Value.FiscalYear.Should().Be("2026-2027");
        calc.Value.TotalEmployeeDeductions.Should().Be(64_300m);
    }

    // ── AC-3: clone fiscal year ──────────────────────────────────────────────
    [Fact]
    public async Task CloneFiscalYear_DuplicatesRulesIntoNewYear()
    {
        var mediator = Pipeline(_tenantA);
        await mediator.Send(IncomeTaxCommand("2025-2026"));
        await mediator.Send(EpfCommand("2025-2026"));

        var clone = await mediator.Send(new CloneFiscalYearCommand(
            "2025-2026", "2026-2027", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)));

        clone.IsSuccess.Should().BeTrue();
        clone.Value!.Should().HaveCount(2);

        var list = await mediator.Send(new ListStatutoryRulesQuery(null, "2026-2027", null, 1, 25));
        list.Value!.TotalCount.Should().Be(2);

        var all = await mediator.Send(new ListStatutoryRulesQuery(null, null, null, 1, 25));
        all.Value!.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task CloneFiscalYear_WhenTargetExists_Returns409()
    {
        var mediator = Pipeline(_tenantA);
        await mediator.Send(IncomeTaxCommand("2025-2026"));
        await mediator.Send(IncomeTaxCommand("2026-2027"));

        var clone = await mediator.Send(new CloneFiscalYearCommand(
            "2025-2026", "2026-2027", new DateOnly(2026, 4, 1), null));

        clone.IsFailure.Should().BeTrue();
        clone.StatusCode.Should().Be(409);
        clone.ErrorCode.Should().Be("target_fiscal_year_exists");
    }

    // ── FR-6: contiguity enforced (gap rejected) ────────────────────────────
    // The FluentValidation rule is unit-tested directly in StatutorySlabRulesTests; here we assert the
    // service's defensive contiguity guard (ValidateShape) also rejects a gap, so an unvalidated caller
    // (e.g. a future internal path) cannot persist non-contiguous slabs.
    [Fact]
    public async Task CreateIncomeTaxRule_WithGap_IsRejectedByService()
    {
        var cmd = new CreateStatutoryRuleCommand(
            StatutoryRuleType.IncomeTax, "PAYE", "LK", "2026-2027",
            new DateOnly(2026, 4, 1), null, true,
            new List<TaxSlabInput>
            {
                new(0m, 250_000m, 0m, 0),
                new(300_000m, null, 10m, 1),
            },
            null);

        var result = await Pipeline(_tenantA).Send(cmd);
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("non_contiguous_slabs");
    }

    // ── AC-5: mandatory statutory component cannot be removed from a structure ─
    [Fact]
    public async Task UpdateStructure_DroppingMandatoryStatutoryComponent_IsBlocked()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;

        Guid structureId, statutoryId, earningId;
        using (var db = new AppDbContext(options, ctx))
        {
            earningId = BaseEntity.NewUuidV7();
            statutoryId = BaseEntity.NewUuidV7();
            structureId = BaseEntity.NewUuidV7();
            db.SalaryComponents.Add(new SalaryComponent { Id = earningId, TenantId = _tenantA, Name = "Basic Salary", Code = "BASIC", Type = SalaryComponentType.Earning, CalculationMethod = CalculationMethod.Fixed, DefaultValue = 50_000m, IsActive = true });
            db.SalaryComponents.Add(new SalaryComponent { Id = statutoryId, TenantId = _tenantA, Name = "EPF", Code = "EPF", Type = SalaryComponentType.Statutory, CalculationMethod = CalculationMethod.PercentageOfBasic, DefaultValue = 12m, IsStatutory = true, IsActive = true });
            db.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = _tenantA, Name = "Std", Code = "STD", EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true });
            db.SalaryStructureComponents.Add(new SalaryStructureComponent { Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, SalaryStructureId = structureId, SalaryComponentId = earningId, ProcessingOrder = 0, IsMandatory = false });
            db.SalaryStructureComponents.Add(new SalaryStructureComponent { Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, SalaryStructureId = structureId, SalaryComponentId = statutoryId, ProcessingOrder = 1, IsMandatory = true });
            await db.SaveChangesAsync();
        }

        var provider = Provider(_tenantA);
        var service = provider.GetRequiredService<ISalaryStructureService>();

        var input = new SalaryStructureInput(
            "Std", "STD", null, new DateOnly(2026, 1, 1), false, true,
            new List<SalaryStructureComponentInput>
            {
                new(earningId, null, null, 0, false),
            });

        var result = await service.UpdateAsync(structureId, input);
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("statutory_mandatory");
    }
}
