// ============================================================================
// TAX-3: YTD-CUMULATIVE income tax (per-country) — integration tests (money-critical).
//
// Cumulative-vs-monthly is a per-country property on the IncomeTax StatutoryRule (IsCumulative). When TRUE the
// rule's tax bands are ANNUAL thresholds and each month withholds tax(cumulative-FY-taxable) − tax-already-
// withheld-YTD (cumulative PAYE true-up). When FALSE (default) each month taxes only that month's income on the
// same bands — today's behaviour, unchanged. TaxableIncome + IncomeTaxWithheld are persisted on EVERY slip.
//
// ANNUAL bands used here: 0–3M @0%, 3M–6M @6%, 6M+ @12%. Employee 1,000,000/month (LK via tenant default).
//   Cumulative: Apr=0, May=0, Jun=0 (cum ≤ 3M), Jul (cum 4M) → 1M@6% = 60,000; Aug (cum 5M, prior 60,000) → 60,000.
//   Non-cumulative: each month taxes 1M on the annual bands = 0 → withheld 0 every month.
//
// PROVIDER: InMemory through the real composed pipeline (same rationale as the sibling statutory tests — the
// verify gate runs `dotnet test` with no PostgreSQL / Docker); the processor's compute is invoked directly.
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

public sealed class YtdCumulativeTaxIntegrationTests
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

    // ── Rule commands: an LK IncomeTax rule with ANNUAL bands + the cumulative flag ──────────────────────────
    private static readonly List<TaxSlabInput> AnnualBands = new()
    {
        new(0m, 3_000_000m, 0m, 0),
        new(3_000_000m, 6_000_000m, 6m, 1),
        new(6_000_000m, null, 12m, 2),
    };

    private static CreateStatutoryRuleCommand LkIncomeTax(bool isCumulative) => new(
        StatutoryRuleType.IncomeTax, "PAYE", "LK", "2026-2027",
        new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true, AnnualBands, null, null, isCumulative);

    /// <summary>Cumulative LK rule carrying a per-period flat exemption (reduces the accumulating taxable base).</summary>
    private static CreateStatutoryRuleCommand LkIncomeTaxWithExemption(decimal flatMonthly) => new(
        StatutoryRuleType.IncomeTax, "PAYE", "LK", "2026-2027",
        new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true, AnnualBands, null,
        new[] { new ExemptionInput("Relief", ExemptionCalculationType.FlatAmount, flatMonthly, null, null, false, 0) },
        true);

    // ── Seeding ──────────────────────────────────────────────────────────────
    private async Task SeedTenantDefaultCountry(Guid tenantId, string cc)
    {
        using var db = Db(tenantId);
        db.Tenants.Add(new Tenant { Id = tenantId, Subdomain = $"t{tenantId:N}".Substring(0, 20), Name = "Co", DefaultCountryCode = cc });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedEmployeeWithSalary(Guid tenantId, string no, decimal monthlyBasic, DateTime? doj = null)
    {
        using var db = Db(tenantId);
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "X",
            Email = $"{no}@t.com", DateOfJoining = doj ?? new DateTime(2020, 1, 1),
            DepartmentId = Guid.Empty, JobTitleId = Guid.Empty, LocationId = null,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        var componentId = BaseEntity.NewUuidV7();
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

    /// <summary>Locks the period, initiates the run, processes it, and returns the run id.</summary>
    private async Task<Guid> RunMonth(IServiceProvider provider, IMediator mediator, int year, int month)
    {
        await LockAttendanceFullyPresent(_tenantA, year, month);
        var init = await mediator.Send(new InitiatePayrollRunCommand(month, year, null));
        init.IsSuccess.Should().BeTrue(init.Error);
        (await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId)).IsSuccess.Should().BeTrue();
        return init.Value.RunId;
    }

    private async Task<PayrollSlip> Slip(Guid empId, int month)
    {
        using var db = Db(_tenantA);
        return await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == empId && s.PayMonth == month);
    }

    /// <summary>Directly persists a slip for a PRIOR period (used to seed a previous-FY slip that must NOT
    /// leak into the new FY's YTD). No run is created — the YTD read only queries PayrollSlips.</summary>
    private async Task SeedRawSlip(Guid empId, int year, int month, decimal taxable, decimal withheld)
    {
        using var db = Db(_tenantA);
        db.PayrollSlips.Add(new PayrollSlip
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, PayrollRunId = BaseEntity.NewUuidV7(),
            EmployeeId = empId, PayYear = year, PayMonth = month,
            GrossEarnings = taxable, TotalDeductions = withheld, NetSalary = taxable - withheld,
            TaxableIncome = taxable, IncomeTaxWithheld = withheld,
            WorkingDays = 22m, PaidDays = 22m, LopDays = 0m,
        });
        await db.SaveChangesAsync();
    }

    // ── Cumulative true-up across months (LOAD-BEARING) ──────────────────────────
    [Fact]
    public async Task Cumulative_WithholdsYtdTrueUp_AcrossMonths()
    {
        await SeedTenantDefaultCountry(_tenantA, "LK");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax(isCumulative: true))).IsSuccess.Should().BeTrue();
        var emp = await SeedEmployeeWithSalary(_tenantA, "C1", 1_000_000m);

        await RunMonth(provider, mediator, 2026, 4);
        await RunMonth(provider, mediator, 2026, 5);
        await RunMonth(provider, mediator, 2026, 6);
        await RunMonth(provider, mediator, 2026, 7);
        await RunMonth(provider, mediator, 2026, 8);

        (await Slip(emp, 4)).IncomeTaxWithheld.Should().Be(0m);      // cum 1M ≤ 3M.
        (await Slip(emp, 5)).IncomeTaxWithheld.Should().Be(0m);      // cum 2M ≤ 3M.
        (await Slip(emp, 6)).IncomeTaxWithheld.Should().Be(0m);      // cum 3M at 0% boundary.
        (await Slip(emp, 7)).IncomeTaxWithheld.Should().Be(60_000m); // cum 4M → 1M@6%; prior 0.
        (await Slip(emp, 8)).IncomeTaxWithheld.Should().Be(60_000m); // cum 5M → 120,000; prior 60,000.

        // Every slip persists THIS month's taxable (1,000,000) regardless of the withheld delta.
        foreach (var m in new[] { 4, 5, 6, 7, 8 })
            (await Slip(emp, m)).TaxableIncome.Should().Be(1_000_000m);

        // The withheld amount is a real deduction on the slip (net = gross − withheld).
        var jul = await Slip(emp, 7);
        jul.TotalDeductions.Should().Be(60_000m);
        jul.NetSalary.Should().Be(940_000m);
    }

    // ── TAX-3 preview follow-up: a cumulative rule previews the FIRST-month figure with no prior-YTD, and the
    //    true-up delta when prior-YTD is supplied — matching the run's July withholding (cum 4M → 60,000). ──
    [Fact]
    public async Task Preview_Cumulative_UsesPriorYtd_MatchesTheRunTrueUp()
    {
        await SeedTenantDefaultCountry(_tenantA, "LK");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax(isCumulative: true))).IsSuccess.Should().BeTrue();

        // No prior-YTD → the cumulative preview shows the first-month figure (cum 1M ≤ 3M → 0).
        var firstMonth = await mediator.Send(new TestStatutoryCalculationQuery(
            1_000_000m, null, 0m, 0m, "2026-2027", "LK"));
        firstMonth.IsSuccess.Should().BeTrue(firstMonth.Error);
        firstMonth.Value!.IncomeTax.Should().Be(0m);

        // With 3,000,000 prior taxable + 0 withheld (mirrors month 7: Apr+May+Jun) → tax(4M) − 0 = 60,000,
        // exactly the run's July withholding — proving the preview now shows the YTD true-up, not month 1.
        var trueUp = await mediator.Send(new TestStatutoryCalculationQuery(
            1_000_000m, null, 0m, 0m, "2026-2027", "LK",
            PriorTaxableIncomeYtd: 3_000_000m, PriorTaxWithheldYtd: 0m));
        trueUp.Value!.IncomeTax.Should().Be(60_000m);

        // And once 60,000 has already been withheld YTD (mirrors month 8), the delta is 0 (120,000 − 60,000 − prior).
        var month8 = await mediator.Send(new TestStatutoryCalculationQuery(
            1_000_000m, null, 0m, 0m, "2026-2027", "LK",
            PriorTaxableIncomeYtd: 4_000_000m, PriorTaxWithheldYtd: 60_000m));
        month8.Value!.IncomeTax.Should().Be(60_000m); // tax(5M)=120,000 − 60,000 withheld = 60,000.
    }

    // ── TAX-3 preview follow-up: the preview EQUALS the run for the same YTD, reconstructed from the ACTUAL
    //    persisted slips (decoupled from the band table — the strongest preview==run proof of the feature). ──
    [Fact]
    public async Task Preview_Cumulative_EqualsTheRun_ForThePersistedYtd()
    {
        await SeedTenantDefaultCountry(_tenantA, "LK");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax(isCumulative: true))).IsSuccess.Should().BeTrue();
        var emp = await SeedEmployeeWithSalary(_tenantA, "X1", 1_000_000m);

        await RunMonth(provider, mediator, 2026, 4);
        await RunMonth(provider, mediator, 2026, 5);
        await RunMonth(provider, mediator, 2026, 6);
        await RunMonth(provider, mediator, 2026, 7);

        // Reconstruct the prior-YTD from the real Apr–Jun slips, then preview July with exactly those inputs.
        decimal priorTaxable = 0m, priorWithheld = 0m;
        foreach (var m in new[] { 4, 5, 6 })
        {
            var s = await Slip(emp, m);
            priorTaxable += s.TaxableIncome;
            priorWithheld += s.IncomeTaxWithheld;
        }
        var julyWithheld = (await Slip(emp, 7)).IncomeTaxWithheld;
        julyWithheld.Should().BeGreaterThan(0m); // guard: July must actually withhold, else the equality is vacuous.

        var preview = await mediator.Send(new TestStatutoryCalculationQuery(
            1_000_000m, null, 0m, 0m, "2026-2027", "LK",
            PriorTaxableIncomeYtd: priorTaxable, PriorTaxWithheldYtd: priorWithheld));
        preview.IsSuccess.Should().BeTrue(preview.Error);
        preview.Value!.IncomeTax.Should().Be(julyWithheld); // preview == what the run actually withheld in July.
    }

    // ── TAX-3: prior-YTD inputs are a NO-OP for a non-cumulative (monthly) rule — kills an if(IsCumulative)→if(true). ──
    [Fact]
    public async Task Preview_NonCumulative_IgnoresPriorYtd()
    {
        await SeedTenantDefaultCountry(_tenantA, "LK");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax(isCumulative: false))).IsSuccess.Should().BeTrue();

        var withoutPrior = await mediator.Send(new TestStatutoryCalculationQuery(
            1_000_000m, null, 0m, 0m, "2026-2027", "LK"));
        var withPrior = await mediator.Send(new TestStatutoryCalculationQuery(
            1_000_000m, null, 0m, 0m, "2026-2027", "LK",
            PriorTaxableIncomeYtd: 3_000_000m, PriorTaxWithheldYtd: 0m));

        withPrior.Value!.IncomeTax.Should().Be(withoutPrior.Value!.IncomeTax); // a monthly rule ignores prior-YTD.
    }

    // ── Non-cumulative (default) unchanged: same annual bands, monthly basis → 0 tax every month ──
    [Fact]
    public async Task NonCumulative_TaxesEachMonthIndependently_OnTheSameBands()
    {
        await SeedTenantDefaultCountry(_tenantA, "LK");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax(isCumulative: false))).IsSuccess.Should().BeTrue();
        var emp = await SeedEmployeeWithSalary(_tenantA, "M1", 1_000_000m);

        await RunMonth(provider, mediator, 2026, 4);
        await RunMonth(provider, mediator, 2026, 7);
        await RunMonth(provider, mediator, 2026, 8);

        // 1,000,000 monthly taxed on the ANNUAL bands = 0 (1M < 3M) → withheld 0 every month (flag gates behaviour).
        (await Slip(emp, 4)).IncomeTaxWithheld.Should().Be(0m);
        (await Slip(emp, 7)).IncomeTaxWithheld.Should().Be(0m);
        (await Slip(emp, 8)).IncomeTaxWithheld.Should().Be(0m);
        (await Slip(emp, 8)).TaxableIncome.Should().Be(1_000_000m); // still persisted.
    }

    // ── Re-run stability: prior months' persisted slips drive the same YTD on a Jul re-run ──
    [Fact]
    public async Task Cumulative_RerunMonth_ReproducesSameWithheld()
    {
        await SeedTenantDefaultCountry(_tenantA, "LK");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax(isCumulative: true))).IsSuccess.Should().BeTrue();
        var emp = await SeedEmployeeWithSalary(_tenantA, "R1", 1_000_000m);

        await RunMonth(provider, mediator, 2026, 4);
        await RunMonth(provider, mediator, 2026, 5);
        await RunMonth(provider, mediator, 2026, 6);
        var julRunId = await RunMonth(provider, mediator, 2026, 7);
        (await Slip(emp, 7)).IncomeTaxWithheld.Should().Be(60_000m);

        // Re-process the SAME Jul run (removes + recomputes its slip). Apr–Jun slips are untouched → same YTD.
        (await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(julRunId)).IsSuccess.Should().BeTrue();
        (await Slip(emp, 7)).IncomeTaxWithheld.Should().Be(60_000m);

        // Exactly ONE active Jul slip must exist — a stale duplicate would double-count into a later month's YTD
        // (the 60,000==60,000 assertion above is numerically blind to that; this catches it directly).
        using var db = Db(_tenantA);
        (await db.PayrollSlips.CountAsync(s => s.EmployeeId == emp && s.PayMonth == 7)).Should().Be(1);
    }

    // ── FY-BOUNDARY RESET (money-critical, recurs every April): a PRIOR-FY slip inside the 13-month lookback
    //    must NOT enter the new FY's cumulative base. Seed a Mar-2026 slip (previous FY) with 5,000,000 taxable;
    //    the LK FY-2026/27 rule starts 1 Apr 2026, so the `period >= EffectiveFrom` filter must exclude it. If
    //    that lower bound were dropped, Apr would see cum 6,000,000 → tax 180,000 instead of 0. ──
    [Fact]
    public async Task Cumulative_PriorFiscalYearSlip_IsExcludedFromNewFyYtd()
    {
        await SeedTenantDefaultCountry(_tenantA, "LK");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax(isCumulative: true))).IsSuccess.Should().BeTrue();
        var emp = await SeedEmployeeWithSalary(_tenantA, "FY1", 1_000_000m);

        // Previous fiscal year (Mar 2026), within the ~13-month lookback but OUTSIDE this FY's window.
        await SeedRawSlip(emp, 2026, 3, taxable: 5_000_000m, withheld: 0m);

        await RunMonth(provider, mediator, 2026, 4); // first month of FY 2026-2027.

        // Apr cumulative = Apr's own 1,000,000 only (≤ 3M) → 0. The 5,000,000 Mar slip did NOT leak in.
        (await Slip(emp, 4)).IncomeTaxWithheld.Should().Be(0m);
        (await Slip(emp, 4)).TaxableIncome.Should().Be(1_000_000m);
    }

    // ── Cumulative base is POST-exemption: a 500,000/month flat exemption on a 2,000,000/month employee →
    //    taxable 1,500,000/month accumulates. Cum: Apr 1.5M, May 3M (0% boundary), Jun 4.5M → 1.5M@6% = 90,000.
    //    (Without the exemption, 2M/month would cross at May → this proves exemptions feed the YTD base.) ──
    [Fact]
    public async Task Cumulative_WithExemption_AccumulatesPostExemptionTaxable()
    {
        await SeedTenantDefaultCountry(_tenantA, "LK");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTaxWithExemption(flatMonthly: 500_000m))).IsSuccess.Should().BeTrue();
        var emp = await SeedEmployeeWithSalary(_tenantA, "EX1", 2_000_000m);

        await RunMonth(provider, mediator, 2026, 4);
        await RunMonth(provider, mediator, 2026, 5);
        await RunMonth(provider, mediator, 2026, 6);

        (await Slip(emp, 4)).TaxableIncome.Should().Be(1_500_000m); // post-exemption (2M − 500k).
        (await Slip(emp, 5)).IncomeTaxWithheld.Should().Be(0m);       // cum 3M at the 0% boundary.
        (await Slip(emp, 6)).IncomeTaxWithheld.Should().Be(90_000m);  // cum 4.5M → 1.5M@6%; prior 0.
    }

    // ── Mid-year joiner: YTD starts at their first earned slip (pre-DOJ months contribute 0) ──
    [Fact]
    public async Task Cumulative_MidYearJoiner_YtdReflectsOnlyOwnSlips()
    {
        await SeedTenantDefaultCountry(_tenantA, "LK");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax(isCumulative: true))).IsSuccess.Should().BeTrue();
        // Joins 1 Jun 2026 at 2,000,000/month. Apr/May are before the DOJ → pro-rated to 0 paid days.
        var emp = await SeedEmployeeWithSalary(_tenantA, "J1", 2_000_000m, doj: new DateTime(2026, 6, 1));

        await RunMonth(provider, mediator, 2026, 4);
        await RunMonth(provider, mediator, 2026, 5);
        await RunMonth(provider, mediator, 2026, 6);
        await RunMonth(provider, mediator, 2026, 7);

        (await Slip(emp, 4)).TaxableIncome.Should().Be(0m); // joined later → nothing earned.
        (await Slip(emp, 5)).TaxableIncome.Should().Be(0m);
        (await Slip(emp, 6)).IncomeTaxWithheld.Should().Be(0m);      // cum 2M ≤ 3M.
        (await Slip(emp, 7)).IncomeTaxWithheld.Should().Be(60_000m); // cum 4M (Jun 2M + Jul 2M) → 1M@6%; Apr/May added nothing.
    }
}
