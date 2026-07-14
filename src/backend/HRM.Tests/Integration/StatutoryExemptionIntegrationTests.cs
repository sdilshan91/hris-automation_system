// ============================================================================
// TAX-2: country-aware CONFIGURABLE TAX EXEMPTIONS — integration tests (money-critical).
//
// An exemption is a child of an IncomeTax StatutoryRule (so it inherits the rule's country + fiscal year +
// effective dates) and reduces the INCOME-TAX taxable base only (never EPF/ETF). It is resolved through the
// SHARED StatutoryDeductionResolver, so the preview (test-calc) and the real run apply the same exemption.
// Covers: FlatAmount (per-period + annual/12), PercentOfGross, PercentOfComponent (proves ComponentAmountsById
// is populated on the run path), the MaxAmount cap, per-country isolation (an LK exemption never touches an IN
// employee), preview==run, and the invariant that an exemption is NOT itself a payslip deduction line.
//
// LK slabs (0/5/20/30 at 250k/500k/1M): tax on a 750k taxable gross = 62,500. Subtracting a 250k exemption →
// taxable 500k → tax 12,500. Subtracting 150k → taxable 600k → tax 32,500. These exact figures anchor the asserts.
//
// PROVIDER: InMemory through the real composed pipeline (same rationale as the sibling statutory tests).
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

public sealed class StatutoryExemptionIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();

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

    private sealed class CapturingScheduler : IPayrollRunJobScheduler
    {
        public string Enqueue(Guid tenantId, string tenantSubdomain, Guid runId) => Guid.NewGuid().ToString();
    }

    private sealed class InMemoryExportStorage : IReportExportStorage
    {
        public Task<string> SaveAsync(Guid tenantId, Guid reportId, string fileName,
            string contentType, byte[] content, CancellationToken cancellationToken = default)
            => Task.FromResult($"mem://{tenantId}/{reportId}/{fileName}");
    }

    private IServiceProvider Provider(Guid tenantId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new MutableTenantContext { TenantId = tenantId });
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = tenantId });
        services.AddSingleton<IPayrollRunJobScheduler, CapturingScheduler>();
        services.AddSingleton<IPayrollNotificationService, LogOnlyPayrollNotificationService>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IOvertimeService, OvertimeService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IAttendanceSummaryService, AttendanceSummaryService>();
        services.AddScoped<IAttendancePayrollService, AttendancePayrollService>();
        services.AddScoped<IReportExportStorage, InMemoryExportStorage>();
        services.AddScoped<IStatutoryDeductionResolver, StatutoryDeductionResolver>();
        services.AddScoped<IPayrollAdjustmentResolver, PayrollAdjustmentResolver>();
        services.AddScoped<IPayrollAuditLogger, PayrollAuditLogger>();
        services.AddScoped<IPayrollSlipCleaner, PayrollSlipCleaner>();
        services.AddScoped<IPayrollRunService, PayrollRunService>();
        services.AddScoped<IPayrollRunProcessor, PayrollRunProcessor>();
        services.AddScoped<IStatutoryRuleService, StatutoryRuleService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(InitiatePayrollRunCommand).Assembly));
        return services.BuildServiceProvider();
    }

    private AppDbContext Db(Guid tenantId) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
        new MutableTenantContext { TenantId = tenantId });

    // ── Rule commands ──────────────────────────────────────────────────────────
    private static readonly List<TaxSlabInput> LkSlabs = new()
    {
        new(0m, 250_000m, 0m, 0),
        new(250_000m, 500_000m, 5m, 1),
        new(500_000m, 1_000_000m, 20m, 2),
        new(1_000_000m, null, 30m, 3),
    };

    private static CreateStatutoryRuleCommand LkIncomeTax(IReadOnlyList<ExemptionInput>? exemptions = null) => new(
        StatutoryRuleType.IncomeTax, "PAYE", "LK", "2026-2027",
        new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true, LkSlabs, null, exemptions);

    /// <summary>A different country's flat 10% regime → tax on 750k = 75,000 (no exemptions).</summary>
    private static CreateStatutoryRuleCommand InIncomeTax() => new(
        StatutoryRuleType.IncomeTax, "Flat Tax", "IN", "2026-2027",
        new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true,
        new List<TaxSlabInput> { new(0m, null, 10m, 0) }, null, null);

    // ── Seeding ────────────────────────────────────────────────────────────────
    private async Task SeedTenantDefaultCountry(Guid tenantId, string cc)
    {
        using var db = Db(tenantId);
        db.Tenants.Add(new Tenant { Id = tenantId, Subdomain = $"t{tenantId:N}".Substring(0, 20), Name = "Co", DefaultCountryCode = cc });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedLocation(Guid tenantId, string cc)
    {
        using var db = Db(tenantId);
        var id = BaseEntity.NewUuidV7();
        db.Locations.Add(new Location { Id = id, TenantId = tenantId, Name = $"Br-{cc}-{id:N}", TimeZone = "Asia/Colombo", CountryCode = cc, IsActive = true, IsDeleted = false });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Single BASIC-only structure so gross == monthlyBasic; returns the employee id. When
    /// <paramref name="basicComponentId"/> is supplied it is used as the SalaryComponent Id (so a
    /// PercentOfComponent exemption can reference it).</summary>
    private async Task<Guid> SeedEmployeeWithSalary(Guid tenantId, string no, decimal monthlyBasic, Guid? locationId = null, Guid? basicComponentId = null)
    {
        using var db = Db(tenantId);
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "X",
            Email = $"{no}@t.com", DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.Empty, JobTitleId = Guid.Empty, LocationId = locationId,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        var componentId = basicComponentId ?? BaseEntity.NewUuidV7();
        db.SalaryComponents.Add(new SalaryComponent
        {
            Id = componentId, TenantId = tenantId, Name = "Basic Salary", Code = "BASIC",
            Type = SalaryComponentType.Earning, CalculationMethod = CalculationMethod.Fixed,
            IsActive = true, ProcessingOrder = 1,
        });
        db.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = empId,
            SalaryStructureId = BaseEntity.NewUuidV7(), SalaryComponentId = componentId,
            AnnualAmount = monthlyBasic * 12m, MonthlyAmount = monthlyBasic,
            EffectiveFrom = new DateOnly(2020, 1, 1), EffectiveTo = null,
        });
        await db.SaveChangesAsync();
        return empId;
    }

    private async Task LockAttendanceFullyPresent(Guid tenantId, int year, int month)
    {
        using var db = Db(tenantId);
        var start = new DateOnly(year, month, 1);
        db.AttendancePeriodLocks.Add(new AttendancePeriodLock
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
            PeriodStart = start, PeriodEnd = start.AddMonths(1).AddDays(-1), IsLocked = true, LockedAt = DateTime.UtcNow,
        });
        var ym = $"{year:D4}-{month:D2}";
        foreach (var empId in await db.Employees.Select(e => e.Id).ToListAsync())
            db.AttendanceMonthlySummaries.Add(new AttendanceMonthlySummary
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = empId,
                YearMonth = ym, TotalPresentDays = 22m, TotalAbsentDays = 0m, LopDays = 0m, TotalOvertimeMinutes = 0, GeneratedAt = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();
    }

    private static ExemptionInput Ex(ExemptionCalculationType type, decimal value, decimal? max = null, bool annual = false, Guid? componentId = null)
        => new("Relief", type, value, componentId, max, annual, 0);

    /// <summary>Runs May-2026 payroll for a single LK-default employee and returns the slip's statutory deduction total.</summary>
    private async Task<(decimal deductions, Guid empId, IServiceProvider provider)> RunSingleLk(IReadOnlyList<ExemptionInput>? exemptions, decimal gross = 750_000m, Guid? basicComponentId = null)
    {
        await SeedTenantDefaultCountry(_tenantA, "LK");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax(exemptions))).IsSuccess.Should().BeTrue();
        var emp = await SeedEmployeeWithSalary(_tenantA, "E1", gross, basicComponentId: basicComponentId);
        await LockAttendanceFullyPresent(_tenantA, 2026, 5);
        var init = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);
        using var db = Db(_tenantA);
        var slip = await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == emp);
        return (slip.TotalDeductions, emp, provider);
    }

    // ── No-exemption baseline: 750k → 62,500 (anchors every reduction below) ──
    [Fact]
    public async Task Baseline_NoExemption_TaxIs62500()
    {
        var (deductions, _, _) = await RunSingleLk(null);
        deductions.Should().Be(62_500m);
    }

    // ── FlatAmount (per-period): 250k exemption → taxable 500k → 12,500 ──
    [Fact]
    public async Task FlatAmount_PerPeriod_ReducesTaxableBase()
    {
        var (deductions, _, _) = await RunSingleLk(new[] { Ex(ExemptionCalculationType.FlatAmount, 250_000m) });
        deductions.Should().Be(12_500m);
    }

    // ── FlatAmount annual 3,000,000 → /12 = 250,000 → 12,500 (proves IsAnnual ÷12) ──
    [Fact]
    public async Task FlatAmount_Annual_IsDividedByTwelve()
    {
        var (deductions, _, _) = await RunSingleLk(new[] { Ex(ExemptionCalculationType.FlatAmount, 3_000_000m, annual: true) });
        deductions.Should().Be(12_500m);
    }

    // ── PercentOfGross 20% of 750k = 150k → taxable 600k → 32,500 ──
    [Fact]
    public async Task PercentOfGross_ReducesTaxableBase()
    {
        var (deductions, _, _) = await RunSingleLk(new[] { Ex(ExemptionCalculationType.PercentOfGross, 20m) });
        deductions.Should().Be(32_500m);
    }

    // ── PercentOfComponent(BASIC) 20% → 150k → 32,500. If ComponentAmountsById were null the exemption
    //    would be 0 and tax 62,500 → this asserts the run path populates the component map. ──
    [Fact]
    public async Task PercentOfComponent_UsesRunPathComponentAmounts()
    {
        var basicId = BaseEntity.NewUuidV7();
        var (deductions, _, _) = await RunSingleLk(
            new[] { Ex(ExemptionCalculationType.PercentOfComponent, 20m, componentId: basicId) },
            basicComponentId: basicId);
        deductions.Should().Be(32_500m);
    }

    // ── Cap: PercentOfGross 50% (=375k) capped at 250k → taxable 500k → 12,500 ──
    [Fact]
    public async Task MaxAmount_CapsTheExemption()
    {
        var (deductions, _, _) = await RunSingleLk(new[] { Ex(ExemptionCalculationType.PercentOfGross, 50m, max: 250_000m) });
        deductions.Should().Be(12_500m);
    }

    // ── An exemption LARGER than gross floors taxable income at 0 → income tax 0 (never negative). Exercises
    //    the ComputeTaxableIncome floor end-to-end (unit-covered, but this proves it through the run/slip). ──
    [Fact]
    public async Task Exemption_ExceedingGross_FloorsTaxAtZero()
    {
        var (deductions, _, _) = await RunSingleLk(new[] { Ex(ExemptionCalculationType.FlatAmount, 800_000m) });
        deductions.Should().Be(0m); // 750k − 800k → floored 0 → no tax.
    }

    // ── Multiple exemptions on one rule SUM (150k + 100k = 250k → taxable 500k → 12,500). Distinct from either
    //    alone (150k→32,500; 100k→42,500), so this kills a `+=`→`=` (last-wins) accumulation mutant. ──
    [Fact]
    public async Task MultipleExemptions_OnOneRule_Sum()
    {
        var (deductions, _, _) = await RunSingleLk(new[]
        {
            Ex(ExemptionCalculationType.FlatAmount, 150_000m),
            new ExemptionInput("Relief2", ExemptionCalculationType.FlatAmount, 100_000m, null, null, false, 1),
        });
        deductions.Should().Be(12_500m); // both subtract: 750k − 250k → 500k → 12,500.
    }

    // ── An exemption reduces the tax base only — it is NOT a payslip deduction line. TotalDeductions must
    //    equal the (reduced) tax exactly, and no line should be typed as an extra statutory deduction beyond tax. ──
    [Fact]
    public async Task Exemption_IsNotAPayslipDeductionLine()
    {
        var (deductions, empId, _) = await RunSingleLk(new[] { Ex(ExemptionCalculationType.FlatAmount, 250_000m) });
        deductions.Should().Be(12_500m); // exactly the reduced tax — an exemption line would inflate this.

        using var db = Db(_tenantA);
        var slip = await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == empId);
        var statutoryLines = await db.PayrollSlipDetails.AsNoTracking()
            .Where(d => d.PayrollSlipId == slip.Id && d.ComponentType == nameof(SalaryComponentType.Statutory)).ToListAsync();
        statutoryLines.Sum(l => l.Amount).Should().Be(12_500m); // only the tax line; the exemption is not among them.
    }

    // ── Per-country isolation: LK rule carries a 250k exemption; IN rule carries none. The LK employee gets
    //    the reduction (12,500); the IN employee is untouched by it (flat 10% of 750k = 75,000). ──
    [Fact]
    public async Task Exemption_IsCountryScoped_DoesNotLeakToOtherCountry()
    {
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax(new[] { Ex(ExemptionCalculationType.FlatAmount, 250_000m) }))).IsSuccess.Should().BeTrue();
        (await mediator.Send(InIncomeTax())).IsSuccess.Should().BeTrue();

        var lkLoc = await SeedLocation(_tenantA, "LK");
        var inLoc = await SeedLocation(_tenantA, "IN");
        var lkEmp = await SeedEmployeeWithSalary(_tenantA, "LKE", 750_000m, locationId: lkLoc);
        var inEmp = await SeedEmployeeWithSalary(_tenantA, "INE", 750_000m, locationId: inLoc);
        await LockAttendanceFullyPresent(_tenantA, 2026, 5);

        var init = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        using var db = Db(_tenantA);
        var lkSlip = await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == lkEmp);
        var inSlip = await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == inEmp);
        lkSlip.TotalDeductions.Should().Be(12_500m);  // LK: exemption applied.
        inSlip.TotalDeductions.Should().Be(75_000m);  // IN: 10% flat, LK exemption did NOT leak.
    }

    // ── Preview == run: the FR-5 test-calc goes through the SAME resolver, so a configured exemption reduces
    //    the previewed income tax to match the paid tax (12,500 on a 750k LK gross with a 250k flat exemption). ──
    [Fact]
    public async Task Preview_ReflectsTheSameConfiguredExemptionAsTheRun()
    {
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax(new[] { Ex(ExemptionCalculationType.FlatAmount, 250_000m) }))).IsSuccess.Should().BeTrue();

        var preview = await mediator.Send(new TestStatutoryCalculationQuery(
            750_000m, null, 0m, 0m, "2026-2027", "LK"));
        preview.IsSuccess.Should().BeTrue(preview.Error);
        preview.Value!.IncomeTax.Should().Be(12_500m);      // exemption applied in the preview.
        preview.Value.TaxableIncome.Should().Be(500_000m);  // 750k − 250k.
    }
}
