// ============================================================================
// DF-2 / ISSUE-300: the FULL cumulative-PAYE RUN true-up on REAL Postgres.
//
// The InMemory arms (YtdCumulativeTaxIntegrationTests) prove the multi-month withholding ARITHMETIC, and
// YtdCumulativeTaxPostgresTests proves the tax COLUMNS + the prior-slip ordinal-window READ round-trip on
// Postgres. The one gap that stayed InMemory-only was the processor actually RUNNING the payroll and
// PERSISTING the withheld deltas over real-Postgres slips end-to-end — the last leg of the repo's standing
// "InMemory-masks-Postgres" class on a money path. This drives the exact composed pipeline the app uses
// (InitiatePayrollRunCommand → PayrollRunProcessor.ProcessAsync) against postgres:17-alpine.
//
// This was previously BLOCKED by the date-Kind bug class (BUG-289/290): with those write sites throwing
// Kind=Unspecified on timestamptz, the surrounding integration paths couldn't run on real Postgres. Those
// landed 2026-07-17; a fresh trace confirmed the payroll run write path itself is date-Kind-clean (every
// DateTime it writes is DateTime.UtcNow), so this arm exercises the run cleanly.
//
// Seed note: on real Postgres the Employee FK to departments/job_titles is enforced (InMemory ignores it),
// so unlike the InMemory sibling this seeds a real Department + JobTitle per tenant instead of Guid.Empty.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Commands;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class YtdCumulativeRunPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantA = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Apply migrations once so the FK/CHECK constraints are live for the whole run.
        var tc = new MutableTenantContext { TenantId = _tenantA };
        await using var db = CreateContext(tc, FakeUser());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

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

    private ICurrentUser FakeUser() => new FakeCurrentUser { TenantId = _tenantA };

    // A tracking Npgsql context on the shared container — mirrors YtdCumulativeTaxPostgresTests.CreateContext.
    private AppDbContext CreateContext(ITenantContext tc, ICurrentUser cu) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);

    private AppDbContext Db() => CreateContext(new MutableTenantContext { TenantId = _tenantA }, FakeUser());

    // The full composed payroll pipeline, backed by the real-Postgres container (mirrors the InMemory
    // sibling's Provider, swapping UseInMemoryDatabase → UseNpgsql + the production interceptors).
    private IServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new MutableTenantContext { TenantId = _tenantA });
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = _tenantA });
        services.AddSingleton<IPayrollRunJobScheduler, CapturingScheduler>();
        services.AddSingleton<IPayrollNotificationService, LogOnlyPayrollNotificationService>();
        services.AddDbContext<AppDbContext>((sp, o) => o
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                new TenantInterceptor(sp.GetRequiredService<ITenantContext>()),
                new AuditInterceptor(sp.GetRequiredService<ICurrentUser>())));
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

    // ── Rule + seeding ──────────────────────────────────────────────────────
    private static readonly List<TaxSlabInput> AnnualBands = new()
    {
        new(0m, 3_000_000m, 0m, 0),
        new(3_000_000m, 6_000_000m, 6m, 1),
        new(6_000_000m, null, 12m, 2),
    };

    private static CreateStatutoryRuleCommand LkIncomeTax(bool isCumulative) => new(
        StatutoryRuleType.IncomeTax, "PAYE", "LK", "2026-2027",
        new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true, AnnualBands, null, null, isCumulative);

    private async Task SeedTenantDefaultCountry(string cc)
    {
        await using var db = Db();
        db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = $"t{_tenantA:N}"[..20], Name = "Co", DefaultCountryCode = cc });
        await db.SaveChangesAsync();
    }

    // On real Postgres the Employee → departments/job_titles FKs are enforced, so seed real rows first.
    private async Task<(Guid deptId, Guid titleId)> SeedDeptAndTitle()
    {
        await using var db = Db();
        var deptId = BaseEntity.NewUuidV7();
        var titleId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = deptId, TenantId = _tenantA, Name = "Engineering", Code = "ENG" });
        db.JobTitles.Add(new JobTitle { Id = titleId, TenantId = _tenantA, TitleName = "Engineer" });
        await db.SaveChangesAsync();
        return (deptId, titleId);
    }

    private async Task<Guid> SeedEmployeeWithSalary(string no, decimal monthlyBasic, Guid deptId, Guid titleId)
    {
        await using var db = Db();
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = _tenantA, EmployeeNo = no, FirstName = no, LastName = "X",
            Email = $"{no}@t.com", DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = deptId, JobTitleId = titleId, LocationId = null,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        var componentId = BaseEntity.NewUuidV7();
        db.SalaryComponents.Add(new SalaryComponent
        {
            Id = componentId, TenantId = _tenantA, Name = "Basic Salary", Code = "BASIC",
            Type = SalaryComponentType.Earning, CalculationMethod = CalculationMethod.Fixed,
            IsActive = true, ProcessingOrder = 1,
        });
        db.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, EmployeeId = empId,
            SalaryStructureId = BaseEntity.NewUuidV7(), SalaryComponentId = componentId,
            AnnualAmount = monthlyBasic * 12m, MonthlyAmount = monthlyBasic,
            EffectiveFrom = new DateOnly(2020, 1, 1), EffectiveTo = null,
        });
        await db.SaveChangesAsync();
        return empId;
    }

    private async Task LockAttendanceFullyPresent(int year, int month)
    {
        await using var db = Db();
        var start = new DateOnly(year, month, 1);
        db.AttendancePeriodLocks.Add(new AttendancePeriodLock
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantA,
            PeriodStart = start, PeriodEnd = start.AddMonths(1).AddDays(-1), IsLocked = true, LockedAt = DateTime.UtcNow,
        });
        var ym = $"{year:D4}-{month:D2}";
        foreach (var empId in await db.Employees.Select(e => e.Id).ToListAsync())
            db.AttendanceMonthlySummaries.Add(new AttendanceMonthlySummary
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, EmployeeId = empId,
                YearMonth = ym, TotalPresentDays = 22m, TotalAbsentDays = 0m, LopDays = 0m,
                TotalOvertimeMinutes = 0, GeneratedAt = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();
    }

    private async Task RunMonth(IServiceProvider provider, IMediator mediator, int year, int month)
    {
        await LockAttendanceFullyPresent(year, month);
        var init = await mediator.Send(new InitiatePayrollRunCommand(month, year, null));
        init.IsSuccess.Should().BeTrue(init.Error);
        (await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId)).IsSuccess.Should().BeTrue();
    }

    // Read back from a FRESH Npgsql context so the assertion reflects what actually hit Postgres.
    private async Task<PayrollSlip> Slip(Guid empId, int month)
    {
        await using var db = Db();
        return await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == empId && s.PayMonth == month);
    }

    // ── The load-bearing arm: multi-month cumulative true-up, PERSISTED and READ BACK on real Postgres ──
    [Fact]
    [Trait("TC", "TC-PAY-016")]
    public async Task Cumulative_WithholdsYtdTrueUp_AcrossMonths_OnPostgres_Df2()
    {
        await SeedTenantDefaultCountry("LK");
        var provider = Provider();
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax(isCumulative: true))).IsSuccess.Should().BeTrue();
        var (deptId, titleId) = await SeedDeptAndTitle();
        var emp = await SeedEmployeeWithSalary("C1", 1_000_000m, deptId, titleId);

        await RunMonth(provider, mediator, 2026, 4);
        await RunMonth(provider, mediator, 2026, 5);
        await RunMonth(provider, mediator, 2026, 6);
        await RunMonth(provider, mediator, 2026, 7);
        await RunMonth(provider, mediator, 2026, 8);

        // The withheld deltas the processor computed + persisted, read back from real Postgres slips.
        (await Slip(emp, 4)).IncomeTaxWithheld.Should().Be(0m);      // cum 1M ≤ 3M.
        (await Slip(emp, 5)).IncomeTaxWithheld.Should().Be(0m);      // cum 2M ≤ 3M.
        (await Slip(emp, 6)).IncomeTaxWithheld.Should().Be(0m);      // cum 3M at the 0% boundary.
        (await Slip(emp, 7)).IncomeTaxWithheld.Should().Be(60_000m); // cum 4M → 1M@6%; prior 0.
        (await Slip(emp, 8)).IncomeTaxWithheld.Should().Be(60_000m); // cum 5M → 120,000; prior 60,000.

        // Every slip persists THIS month's taxable regardless of the withheld delta.
        foreach (var m in new[] { 4, 5, 6, 7, 8 })
            (await Slip(emp, m)).TaxableIncome.Should().Be(1_000_000m);

        // The withheld amount is a real deduction on the July slip (net = gross − withheld).
        var jul = await Slip(emp, 7);
        jul.TotalDeductions.Should().Be(60_000m);
        jul.NetSalary.Should().Be(940_000m);
    }

    // ── Flag-gate contrast on the real provider: the SAME annual bands with IsCumulative=false withhold 0
    //    every month (1M/month < 3M taxed monthly) — kills an if(IsCumulative)→if(true) that the numeric
    //    cumulative arm alone cannot distinguish. ──
    [Fact]
    [Trait("TC", "TC-PAY-016")]
    public async Task NonCumulative_TaxesEachMonthIndependently_OnPostgres_Df2()
    {
        await SeedTenantDefaultCountry("LK");
        var provider = Provider();
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax(isCumulative: false))).IsSuccess.Should().BeTrue();
        var (deptId, titleId) = await SeedDeptAndTitle();
        var emp = await SeedEmployeeWithSalary("M1", 1_000_000m, deptId, titleId);

        await RunMonth(provider, mediator, 2026, 4);
        await RunMonth(provider, mediator, 2026, 7);
        await RunMonth(provider, mediator, 2026, 8);

        (await Slip(emp, 4)).IncomeTaxWithheld.Should().Be(0m);
        (await Slip(emp, 7)).IncomeTaxWithheld.Should().Be(0m); // no YTD accumulation → never crosses 3M.
        (await Slip(emp, 8)).IncomeTaxWithheld.Should().Be(0m);
        (await Slip(emp, 8)).TaxableIncome.Should().Be(1_000_000m);
    }
}
