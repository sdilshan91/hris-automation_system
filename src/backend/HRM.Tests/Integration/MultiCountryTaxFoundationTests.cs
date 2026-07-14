// ============================================================================
// MULTI-COUNTRY TAX FOUNDATION — integration tests (money-critical).
//
// An employee is taxed under their BRANCH/Location's country; the tenant DEFAULT country is the fallback; when
// neither resolves the employee's statutory deductions are SKIPPED and they are FLAGGED on the run (never taxed
// under the wrong/no country). Covers:
//   * Country selection (LOAD-BEARING): two IncomeTax rules, same tenant+FY, DIFFERENT countries — each
//     employee gets THEIR country's tax. This FAILS on the pre-fix resolver (which grouped by rule-type only and
//     returned one country's rule for everyone).
//   * Fallback: no-location employee uses the tenant DefaultCountryCode's rules.
//   * No-country: no location AND no tenant default → statutory skipped + flagged; non-statutory pay still computed.
//   * Uniqueness: a 2nd overlapping (tenant, type, country, FY) rule → 409; a different-country rule → allowed.
//   * Per-country clone: clone country A's FY while country B already has rules in the target FY → succeeds.
//   * Location CountryCode round-trips (create + update, normalized upper-case).
//
// PROVIDER: InMemory through the real composed pipeline — same rationale as the sibling Payroll/Statutory
// integration tests (the verify gate runs `dotnet test` with no PostgreSQL / Docker); the processor's compute is
// invoked directly (no live Hangfire).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Locations.Commands;
using HRM.Application.Features.Locations.Queries;
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

public sealed class MultiCountryTaxFoundationTests
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
        services.AddScoped<ILocationService, LocationService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(InitiatePayrollRunCommand).Assembly));
        return services.BuildServiceProvider();
    }

    private AppDbContext Db(Guid tenantId) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
        new MutableTenantContext { TenantId = tenantId });

    // ── Rule commands ────────────────────────────────────────────────────────

    /// <summary>Sri-Lanka-style progressive slabs: 0/5/20/30% → tax on 750k = 62,500.</summary>
    private static CreateStatutoryRuleCommand LkIncomeTax(string fiscalYear,
        DateOnly? from = null, DateOnly? to = null) => new(
        StatutoryRuleType.IncomeTax, "PAYE", "LK", fiscalYear,
        from ?? new DateOnly(2026, 4, 1), to ?? new DateOnly(2027, 3, 31), true,
        new List<TaxSlabInput>
        {
            new(0m, 250_000m, 0m, 0),
            new(250_000m, 500_000m, 5m, 1),
            new(500_000m, 1_000_000m, 20m, 2),
            new(1_000_000m, null, 30m, 3),
        },
        null);

    /// <summary>A DIFFERENT country's regime: a flat 10% single slab → tax on 750k = 75,000 (≠ LK's 62,500).</summary>
    private static CreateStatutoryRuleCommand InIncomeTax(string fiscalYear) => new(
        StatutoryRuleType.IncomeTax, "Flat Tax", "IN", fiscalYear,
        new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), true,
        new List<TaxSlabInput> { new(0m, null, 10m, 0) },
        null);

    // ── Seeding ──────────────────────────────────────────────────────────────

    private async Task<Guid> SeedLocation(Guid tenantId, string? countryCode)
    {
        using var db = Db(tenantId);
        var id = BaseEntity.NewUuidV7();
        db.Locations.Add(new Location
        {
            Id = id, TenantId = tenantId, Name = $"Branch-{countryCode ?? "none"}-{id:N}",
            TimeZone = "Asia/Colombo", CountryCode = countryCode, IsActive = true, IsDeleted = false,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedEmployeeWithSalary(Guid tenantId, string no, decimal monthlyBasic, Guid? locationId)
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

    private async Task SeedTenantDefaultCountry(Guid tenantId, string? defaultCountryCode)
    {
        using var db = Db(tenantId);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Subdomain = $"t{tenantId:N}".Substring(0, 20), Name = "Test Co",
            DefaultCountryCode = defaultCountryCode,
        });
        await db.SaveChangesAsync();
    }

    private async Task LockAttendanceFullyPresent(Guid tenantId, int year, int month)
    {
        using var db = Db(tenantId);
        var start = new DateOnly(year, month, 1);
        db.AttendancePeriodLocks.Add(new AttendancePeriodLock
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
            PeriodStart = start, PeriodEnd = start.AddMonths(1).AddDays(-1),
            IsLocked = true, LockedAt = DateTime.UtcNow,
        });
        var yearMonth = $"{year:D4}-{month:D2}";
        var employeeIds = await db.Employees.Select(e => e.Id).ToListAsync();
        foreach (var empId in employeeIds)
        {
            db.AttendanceMonthlySummaries.Add(new AttendanceMonthlySummary
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = empId,
                YearMonth = yearMonth, TotalPresentDays = 22m, TotalAbsentDays = 0m,
                LopDays = 0m, TotalOvertimeMinutes = 0, GeneratedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
    }

    // ── Country selection (LOAD-BEARING): two countries' rules must NOT collide ──

    [Fact]
    public async Task TwoCountries_SameTypeAndFy_EachEmployeeTaxedUnderTheirOwnCountry()
    {
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        // Two IncomeTax rules, SAME tenant + FY, DIFFERENT countries (different-country create is allowed).
        (await mediator.Send(LkIncomeTax("2026-2027"))).IsSuccess.Should().BeTrue();
        (await mediator.Send(InIncomeTax("2026-2027"))).IsSuccess.Should().BeTrue();

        var lkLoc = await SeedLocation(_tenantA, "LK");
        var inLoc = await SeedLocation(_tenantA, "IN");
        var empLk = await SeedEmployeeWithSalary(_tenantA, "LK1", 750_000m, lkLoc);
        var empIn = await SeedEmployeeWithSalary(_tenantA, "IN1", 750_000m, inLoc);
        await LockAttendanceFullyPresent(_tenantA, 2026, 5);

        var init = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        using var db = Db(_tenantA);
        var lkSlip = await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == empLk);
        var inSlip = await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == empIn);

        // The LK employee pays LK progressive tax; the IN employee pays IN flat tax. On the PRE-FIX resolver
        // (rule-type-only grouping) BOTH would have received the same single rule → this assertion would fail.
        lkSlip.TotalDeductions.Should().Be(62_500m);
        inSlip.TotalDeductions.Should().Be(75_000m);
        lkSlip.NetSalary.Should().Be(687_500m);
        inSlip.NetSalary.Should().Be(675_000m);
    }

    // ── Fallback: no-location employee uses the tenant DefaultCountryCode ──

    [Fact]
    public async Task NoLocation_UsesTenantDefaultCountry()
    {
        await SeedTenantDefaultCountry(_tenantA, "LK");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax("2026-2027"))).IsSuccess.Should().BeTrue();

        var emp = await SeedEmployeeWithSalary(_tenantA, "ND1", 750_000m, locationId: null);
        await LockAttendanceFullyPresent(_tenantA, 2026, 5);

        var init = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        using var db = Db(_tenantA);
        var slip = await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == emp);
        slip.TotalDeductions.Should().Be(62_500m); // LK tax via the tenant default.
    }

    // ── No country in a MULTI-country tenant: no location AND no tenant default → statutory SKIPPED + FLAGGED,
    //    non-statutory still computed. (The tenant must be genuinely multi-country — with rules for two countries
    //    the sole-country backward-compat fallback does NOT apply, so the employee's country is truly ambiguous.
    //    A SINGLE-country tenant instead falls back to its one country; that path is proved elsewhere.) ──

    [Fact]
    public async Task NoLocation_NoTenantDefault_SkipsStatutory_AndFlagsEmployee()
    {
        // No tenant row seeded → tenant default is null.
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        // Rules for TWO countries → no single-country fallback → a country-less employee is genuinely ambiguous.
        (await mediator.Send(LkIncomeTax("2026-2027"))).IsSuccess.Should().BeTrue(); // statutory IS configured.
        (await mediator.Send(InIncomeTax("2026-2027"))).IsSuccess.Should().BeTrue();

        var emp = await SeedEmployeeWithSalary(_tenantA, "NC1", 750_000m, locationId: null);
        await LockAttendanceFullyPresent(_tenantA, 2026, 5);

        var init = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        using var db = Db(_tenantA);
        var slip = await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == emp);
        slip.TotalDeductions.Should().Be(0m);      // NO tax applied (never guessed a country).
        slip.GrossEarnings.Should().Be(750_000m);  // non-statutory pay STILL computed.
        slip.NetSalary.Should().Be(750_000m);

        var run = await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == init.Value.RunId);
        run.RunLog.Should().Contain("WARNING");
        run.RunLog.Should().Contain("NC1");
        run.RunLog.Should().Contain("statutory deductions SKIPPED");
    }

    // ── Backward-compat: a SINGLE-country tenant taxes a country-less employee under its ONE rule country ──
    //    (no location, no tenant default) — so existing single-country tenants keep deducting statutory with
    //    ZERO setup instead of silently under-taxing. Fallback applies ONLY when rules span exactly one country. ──

    [Fact]
    public async Task NoLocation_NoTenantDefault_SingleCountryTenant_FallsBackToSoleCountry()
    {
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax("2026-2027"))).IsSuccess.Should().BeTrue(); // the tenant's ONLY country.

        var emp = await SeedEmployeeWithSalary(_tenantA, "SC1", 750_000m, locationId: null);
        await LockAttendanceFullyPresent(_tenantA, 2026, 5);

        var init = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        using var db = Db(_tenantA);
        var slip = await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == emp);
        slip.TotalDeductions.Should().Be(62_500m); // LK tax applied via the sole-country fallback (not skipped).
    }

    // ── Resolved-but-unconfigured country: an employee is coded for a country the tenant has NO rules for
    //    (here US, tenant runs LK+IN). We must NEVER apply another country's template statutory → SKIP + FLAG,
    //    exactly like the null-country path. ──

    [Fact]
    public async Task ResolvedCountry_NoRulesForIt_SkipsStatutory_AndFlagsEmployee()
    {
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax("2026-2027"))).IsSuccess.Should().BeTrue();
        (await mediator.Send(InIncomeTax("2026-2027"))).IsSuccess.Should().BeTrue();

        var usLocation = await SeedLocation(_tenantA, "US"); // a country with NO configured rules.
        var emp = await SeedEmployeeWithSalary(_tenantA, "US1", 750_000m, locationId: usLocation);
        await LockAttendanceFullyPresent(_tenantA, 2026, 5);

        var init = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        using var db = Db(_tenantA);
        var slip = await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == emp);
        slip.TotalDeductions.Should().Be(0m);      // never taxed under LK's or IN's rules.
        slip.GrossEarnings.Should().Be(750_000m);
        slip.NetSalary.Should().Be(750_000m);

        var run = await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == init.Value.RunId);
        run.RunLog.Should().Contain("WARNING");
        run.RunLog.Should().Contain("US1");
        run.RunLog.Should().Contain("statutory deductions SKIPPED");
        run.RunLog.Should().Contain("'US'"); // the country-specific reason.
    }

    // ── Uniqueness: overlapping duplicate → 409; different-country → allowed ──

    [Fact]
    public async Task Duplicate_SameCountryTypeFy_Overlapping_Returns409_DifferentCountry_Allowed()
    {
        var mediator = Provider(_tenantA).GetRequiredService<IMediator>();

        (await mediator.Send(LkIncomeTax("2026-2027"))).IsSuccess.Should().BeTrue();

        // 2nd LK IncomeTax rule for the SAME FY with an OVERLAPPING window → rejected.
        var dup = await mediator.Send(LkIncomeTax("2026-2027",
            from: new DateOnly(2026, 6, 1), to: new DateOnly(2027, 3, 31)));
        dup.IsFailure.Should().BeTrue();
        dup.StatusCode.Should().Be(409);
        dup.ErrorCode.Should().Be("duplicate_statutory_rule");

        // A DIFFERENT country's IncomeTax rule for the same FY is allowed (the point of multi-country).
        (await mediator.Send(InIncomeTax("2026-2027"))).IsSuccess.Should().BeTrue();
    }

    // ── Intra-FY versioning: two NON-overlapping versions of the same (type, country, FY) both persist, and
    //    the resolver picks the one in effect for the pay period (e.g. a mid-year rate change). The unique index
    //    includes effective_from, so it forbids only a TRUE duplicate — it must NOT block versioning. ──

    [Fact]
    public async Task IntraFiscalYear_TwoNonOverlappingVersions_BothCreated_ResolverPicksEffectiveVersion()
    {
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        // Same (type, country, FY) but DIFFERENT, non-overlapping effective windows (H1 5% vs H2 10%).
        var h1 = await mediator.Send(new CreateStatutoryRuleCommand(
            StatutoryRuleType.IncomeTax, "PAYE H1", "LK", "2025",
            new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30), true,
            new List<TaxSlabInput> { new(0m, null, 5m, 0) }, null));
        h1.IsSuccess.Should().BeTrue(h1.Error);
        var h2 = await mediator.Send(new CreateStatutoryRuleCommand(
            StatutoryRuleType.IncomeTax, "PAYE H2", "LK", "2025",
            new DateOnly(2025, 7, 1), new DateOnly(2025, 12, 31), true,
            new List<TaxSlabInput> { new(0m, null, 10m, 0) }, null));
        h2.IsSuccess.Should().BeTrue(h2.Error); // intra-FY versioning is NOT blocked by the unique index.

        var lkLoc = await SeedLocation(_tenantA, "LK");
        var emp = await SeedEmployeeWithSalary(_tenantA, "V1", 750_000m, lkLoc);
        await LockAttendanceFullyPresent(_tenantA, 2025, 3);
        await LockAttendanceFullyPresent(_tenantA, 2025, 9);

        var marInit = await mediator.Send(new InitiatePayrollRunCommand(3, 2025, null)); // H1 window.
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(marInit.Value!.RunId);
        var sepInit = await mediator.Send(new InitiatePayrollRunCommand(9, 2025, null)); // H2 window.
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(sepInit.Value!.RunId);

        using var db = Db(_tenantA);
        var marSlip = await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == emp && s.PayMonth == 3);
        var sepSlip = await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == emp && s.PayMonth == 9);
        marSlip.TotalDeductions.Should().Be(37_500m); // resolver picked the H1 5% version.
        sepSlip.TotalDeductions.Should().Be(75_000m); // resolver picked the H2 10% version.
    }

    // ── Per-country clone: clone country A's FY while country B already has rules in the target FY ──

    [Fact]
    public async Task CloneFiscalYear_IsPerCountry_SucceedsWhenAnotherCountryOccupiesTargetFy()
    {
        var mediator = Provider(_tenantA).GetRequiredService<IMediator>();

        // Country LK has rules in the SOURCE FY 2025-2026.
        (await mediator.Send(LkIncomeTax("2025-2026",
            from: new DateOnly(2025, 4, 1), to: new DateOnly(2026, 3, 31)))).IsSuccess.Should().BeTrue();
        // Country IN already occupies the TARGET FY 2026-2027.
        (await mediator.Send(InIncomeTax("2026-2027"))).IsSuccess.Should().BeTrue();

        // Cloning LK's 2025-2026 into 2026-2027 must SUCCEED (IN's presence in the target FY must not block LK).
        var clone = await mediator.Send(new CloneFiscalYearCommand(
            "2025-2026", "2026-2027", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31)));
        clone.IsSuccess.Should().BeTrue(clone.Error);
        clone.Value!.Should().HaveCount(1); // just the one LK rule cloned.

        using var db = Db(_tenantA);
        var targetRules = await db.StatutoryRules.AsNoTracking().Where(r => r.FiscalYear == "2026-2027").ToListAsync();
        targetRules.Select(r => r.CountryCode).Should().BeEquivalentTo(new[] { "IN", "LK" });
    }

    // ── Location CountryCode round-trips (create + update, normalized upper-case) ──

    [Fact]
    public async Task LocationCountryCode_RoundTrips_AndNormalizesToUpperCase()
    {
        var mediator = Provider(_tenantA).GetRequiredService<IMediator>();

        var created = await mediator.Send(new CreateLocationCommand(
            "HQ", null, null, null, null, "Sri Lanka", null, "Asia/Colombo", null, CountryCode: "lk"));
        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value!.CountryCode.Should().Be("LK"); // normalized on write.

        var fetched = await mediator.Send(new GetLocationByIdQuery(created.Value.Id));
        fetched.Value!.CountryCode.Should().Be("LK");

        var updated = await mediator.Send(new UpdateLocationCommand(
            created.Value.Id, "HQ", null, null, null, null, "India", null, "Asia/Colombo", null, CountryCode: "in"));
        updated.IsSuccess.Should().BeTrue(updated.Error);
        updated.Value!.CountryCode.Should().Be("IN");
    }

    // ── TAX-3 normalization guard: the write path always upper-cases, but a raw/seed/import write could bypass
    //    the service and store an un-normalized CountryCode. The resolver normalizes the STORED side too, so such
    //    a dirty-cased rule still resolves (else the cumulative pre-scan would thread prior-YTD while the resolver
    //    silently matched nothing — a money-path divergence). Baseline-vs-corrupted so it doesn't couple to bands. ──
    [Fact]
    public async Task Resolver_MatchesAnUnNormalizedStoredCountryCode()
    {
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        (await mediator.Send(LkIncomeTax("2026-2027"))).IsSuccess.Should().BeTrue();

        // Baseline: the correctly-cased "LK" rule resolves and produces real tax.
        var baseline = await mediator.Send(new TestStatutoryCalculationQuery(750_000m, null, 0m, 0m, "2026-2027", "LK"));
        baseline.IsSuccess.Should().BeTrue(baseline.Error);
        baseline.Value!.IncomeTax.Should().BeGreaterThan(0m);

        // Simulate a raw/import write that bypassed the service's normalize-on-save → the stored code is lower-case.
        using (var db = Db(_tenantA))
        {
            var rule = await db.StatutoryRules.SingleAsync(r => r.CountryCode == "LK");
            rule.CountryCode = "lk";
            await db.SaveChangesAsync();
        }

        // The normalized "LK" preview must STILL resolve the dirty-cased rule to the identical tax (not empty).
        var afterCorruption = await mediator.Send(new TestStatutoryCalculationQuery(750_000m, null, 0m, 0m, "2026-2027", "LK"));
        afterCorruption.IsSuccess.Should().BeTrue(afterCorruption.Error);
        afterCorruption.Value!.IncomeTax.Should().Be(baseline.Value!.IncomeTax);
    }
}
