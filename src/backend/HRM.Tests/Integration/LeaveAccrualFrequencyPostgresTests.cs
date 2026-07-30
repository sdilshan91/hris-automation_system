// ============================================================================
// BUG-291 (HIGH — money): LeaveType.AccrualFrequency was settable by a tenant admin but READ BY NOTHING in
// the accrual path. ProcessSingleAccrualAsync credited the whole annual entitlement in ONE ledger row and its
// idempotency guard was scoped to (employee, type, year, EntryType==Accrual) — so the first run of the year
// credited 12/12 and every later run was skipped. A Monthly/Quarterly type behaved exactly like Yearly, and
// the inflated balance was ENCASHED / paid in F&F (LeaveEncashmentService + RealPayrollFnFIntegration both
// read this ledger). An employee leaving in March under a Monthly policy carried a full year's balance.
//
// The fix makes accrual frequency-aware: each period credits entitlement/periods-per-year exactly once, keyed
// on the new LeaveLedger.AccrualPeriod column (Monthly=12, Quarterly=4, Yearly/Upfront=1). These arms run on
// REAL PostgreSQL because this repo's standing rule is that InMemory masks Postgres on ledger arithmetic —
// the numeric(5,2) column rounding (which the cumulative-difference amount math must survive without drift)
// only manifests on real PG, and prior money bugs (DF-65, BUG-124) were only caught there.
//
// Arms:
//   (1) Monthly_CreditsOnePeriodPerElapsedMonth_SumsToAnnual — Jan run = 1/12, Feb = 2/12, …, twelve runs =
//       EXACTLY the annual entitlement, no over/under, no duplicates; re-run within a month is a no-op.
//   (2) Monthly_NonDivisibleEntitlement_SumsExactlyToAnnual — 10 days / 12 → per-period rounds to 2dp but the
//       cumulative-difference math still sums to exactly 10.00 (kills the numeric(5,2) drift a naive
//       entitlement/12 would introduce).
//   (3) Quarterly_CreditsFourPeriods — Q1..Q4, idempotent within a quarter, back-fill lands the full year.
//   (4) Yearly_IsByteIdentical_SingleFullEntitlementRow — one row, full amount, "Annual accrual" label, re-run
//       no-op (the no-regression contract for every existing Annual/Upfront tenant).
//   (5) Monthly_MidYearJoiner_And_FteBelowOne_Compose_NoDoubleDiscount — the joiner+FTE pro-ration is applied
//       ONCE (via proratedAnnual); a full year of Monthly periods sums to EXACTLY that single proratedAnnual.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-LV-002-ACCRUAL-FREQ")] // BUG-291: AccrualFrequency must drive the accrual credit (US-LV-002 FR-5).
public sealed class LeaveAccrualFrequencyPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // A settable clock — the accrual output now depends on "today" (how many periods have elapsed), so the
    // month-by-month progression is driven by advancing this between runs.
    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public MutableClock(DateTimeOffset now) => Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
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

    private AppDbContext CreateContext()
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);
    }

    private LeaveEntitlementService BuildService(AppDbContext db, TimeProvider clock)
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        return new LeaveEntitlementService(
            db, tc, cu, NullLogger<LeaveEntitlementService>.Instance,
            new TenantLeaveYearResolver(db, tc), recalcScheduler: null, timeProvider: clock);
    }

    private async Task<(Guid deptId, Guid jobId)> SeedTenantScaffoldAsync(AppDbContext db)
    {
        var deptId = BaseEntity.NewUuidV7();
        var jobId = BaseEntity.NewUuidV7();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Subdomain = "acme", Name = "Acme", DefaultCountryCode = "LK",
            FiscalYearStartMonth = 1, // calendar tenant → leave year 2026 = 2026-01-01..2026-12-31.
        });
        db.Departments.Add(new Department { Id = deptId, TenantId = _tenantId, Name = "Ops", Code = "OPS", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = jobId, TenantId = _tenantId, TitleName = "Engineer", IsActive = true });
        await db.SaveChangesAsync();
        return (deptId, jobId);
    }

    private static Employee NewEmployee(
        Guid tenantId, Guid deptId, Guid jobId, string no, DateTime doj, decimal fte = 1.0m) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeNo = no, FirstName = "P", LastName = no,
        Email = $"{no}@t.com", DateOfJoining = doj, DepartmentId = deptId, JobTitleId = jobId,
        EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true, Fte = fte,
    };

    private static LeaveType NewLeaveType(Guid tenantId, decimal annual, AccrualFrequency freq, string name) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = name, AnnualEntitlement = annual,
        AccrualFrequency = freq, Gender = LeaveTypeGender.All, IsActive = true,
    };

    private static async Task<(decimal Sum, List<int> Periods, int Count)> AccrualSummaryAsync(
        AppDbContext db, Guid empId, Guid typeId)
    {
        var rows = await db.LeaveLedgerEntries.AsNoTracking()
            .Where(l => l.EmployeeId == empId && l.LeaveTypeId == typeId
                        && l.EntryType == LedgerEntryType.Accrual)
            .ToListAsync();
        return (rows.Sum(r => r.Amount), rows.Select(r => r.AccrualPeriod ?? 0).OrderBy(p => p).ToList(), rows.Count);
    }

    // ── (1) Monthly: one period per elapsed month, twelve runs sum to exactly the annual entitlement ────────
    [Fact]
    public async Task Monthly_CreditsOnePeriodPerElapsedMonth_SumsToAnnual_OnPostgres()
    {
        await using (var mig = CreateContext()) await mig.Database.MigrateAsync();

        Guid empId, typeId;
        await using (var seed = CreateContext())
        {
            var (deptId, jobId) = await SeedTenantScaffoldAsync(seed);
            var emp = NewEmployee(_tenantId, deptId, jobId, "PG-M", new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            empId = emp.Id;
            seed.Employees.Add(emp);
            var lt = NewLeaveType(_tenantId, annual: 24m, AccrualFrequency.Monthly, "Monthly-24"); // 24/12 = 2.00/mo
            typeId = lt.Id;
            seed.LeaveTypes.Add(lt);
            await seed.SaveChangesAsync();
        }

        var clock = new MutableClock(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));

        // January run — must credit ONE period (1/12), NOT the whole year.
        await using (var run = CreateContext())
            await BuildService(run, clock).ProcessAccrualsAsync(2026, typeId);
        await using (var check = CreateContext())
        {
            var (sum, periods, count) = await AccrualSummaryAsync(check, empId, typeId);
            count.Should().Be(1, "the January run credits exactly the first monthly period");
            periods.Should().Equal(1);
            sum.Should().Be(2m, "1/12 of 24 = 2 — not the whole 24 (the BUG-291 over-credit)");
        }

        // February run — a SECOND period (2/12 cumulative). The pre-fix year-scoped guard would skip this.
        clock.Now = new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);
        await using (var run = CreateContext())
            await BuildService(run, clock).ProcessAccrualsAsync(2026, typeId);
        await using (var check = CreateContext())
        {
            var (sum, periods, count) = await AccrualSummaryAsync(check, empId, typeId);
            count.Should().Be(2);
            periods.Should().Equal(1, 2);
            sum.Should().Be(4m, "after Feb the balance is 2/12 of 24 = 4");
        }

        // Re-run within February — idempotent at PERIOD granularity, no duplicate row.
        await using (var run = CreateContext())
            await BuildService(run, clock).ProcessAccrualsAsync(2026, typeId);
        await using (var check = CreateContext())
        {
            var (sum, _, count) = await AccrualSummaryAsync(check, empId, typeId);
            count.Should().Be(2, "re-running in the same period must not add a duplicate accrual");
            sum.Should().Be(4m);
        }

        // Run the remaining months (Mar..Dec).
        for (int month = 3; month <= 12; month++)
        {
            clock.Now = new DateTimeOffset(2026, month, 15, 0, 0, 0, TimeSpan.Zero);
            await using var run = CreateContext();
            await BuildService(run, clock).ProcessAccrualsAsync(2026, typeId);
        }
        await using (var check = CreateContext())
        {
            var (sum, periods, count) = await AccrualSummaryAsync(check, empId, typeId);
            count.Should().Be(12, "one accrual row per month, no duplicates");
            periods.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
            sum.Should().Be(24m, "twelve monthly periods sum to EXACTLY the annual entitlement — no over/under-credit");
        }
    }

    // ── (2) Monthly, non-divisible entitlement: 10/12 rounds per period but sums to EXACTLY 10.00 ───────────
    [Fact]
    public async Task Monthly_NonDivisibleEntitlement_SumsExactlyToAnnual_OnPostgres()
    {
        await using (var mig = CreateContext()) await mig.Database.MigrateAsync();

        Guid empId, typeId;
        await using (var seed = CreateContext())
        {
            var (deptId, jobId) = await SeedTenantScaffoldAsync(seed);
            var emp = NewEmployee(_tenantId, deptId, jobId, "PG-M10", new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            empId = emp.Id;
            seed.Employees.Add(emp);
            var lt = NewLeaveType(_tenantId, annual: 10m, AccrualFrequency.Monthly, "Monthly-10"); // 10/12 = 0.8333…
            typeId = lt.Id;
            seed.LeaveTypes.Add(lt);
            await seed.SaveChangesAsync();
        }

        var clock = new MutableClock(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        for (int month = 1; month <= 12; month++)
        {
            clock.Now = new DateTimeOffset(2026, month, 15, 0, 0, 0, TimeSpan.Zero);
            await using var run = CreateContext();
            await BuildService(run, clock).ProcessAccrualsAsync(2026, typeId);
        }

        await using (var check = CreateContext())
        {
            var (sum, periods, count) = await AccrualSummaryAsync(check, empId, typeId);
            count.Should().Be(12);
            periods.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
            sum.Should().Be(10m,
                "cumulative-difference amounts survive numeric(5,2): 12 rounded per-period rows still sum to exactly 10.00");
            // The running BalanceAfter chain must end on the exact annual entitlement too.
            var last = await check.LeaveLedgerEntries.AsNoTracking()
                .Where(l => l.EmployeeId == empId && l.LeaveTypeId == typeId && l.EntryType == LedgerEntryType.Accrual)
                .OrderByDescending(l => l.AccrualPeriod)
                .FirstAsync();
            last.BalanceAfter.Should().Be(10m);
        }
    }

    // ── (3) Quarterly: four periods, idempotent within a quarter, back-fill lands the full year ─────────────
    [Fact]
    public async Task Quarterly_CreditsFourPeriods_OnPostgres()
    {
        await using (var mig = CreateContext()) await mig.Database.MigrateAsync();

        Guid empId, typeId;
        await using (var seed = CreateContext())
        {
            var (deptId, jobId) = await SeedTenantScaffoldAsync(seed);
            var emp = NewEmployee(_tenantId, deptId, jobId, "PG-Q", new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            empId = emp.Id;
            seed.Employees.Add(emp);
            var lt = NewLeaveType(_tenantId, annual: 12m, AccrualFrequency.Quarterly, "Quarterly-12"); // 12/4 = 3/qtr
            typeId = lt.Id;
            seed.LeaveTypes.Add(lt);
            await seed.SaveChangesAsync();
        }

        var clock = new MutableClock(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));

        // Q1 (January).
        await using (var run = CreateContext())
            await BuildService(run, clock).ProcessAccrualsAsync(2026, typeId);
        // Still Q1 (February) — no new period.
        clock.Now = new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);
        await using (var run = CreateContext())
            await BuildService(run, clock).ProcessAccrualsAsync(2026, typeId);
        await using (var check = CreateContext())
        {
            var (sum, periods, count) = await AccrualSummaryAsync(check, empId, typeId);
            count.Should().Be(1, "Feb is still the first quarter — no second accrual");
            periods.Should().Equal(1);
            sum.Should().Be(3m, "one quarter of 12 = 3");
        }

        // Q2 (April).
        clock.Now = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);
        await using (var run = CreateContext())
            await BuildService(run, clock).ProcessAccrualsAsync(2026, typeId);
        await using (var check = CreateContext())
        {
            var (sum, periods, _) = await AccrualSummaryAsync(check, empId, typeId);
            periods.Should().Equal(1, 2);
            sum.Should().Be(6m);
        }

        // Year-end — back-fill Q3 + Q4 in one run.
        clock.Now = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        await using (var run = CreateContext())
            await BuildService(run, clock).ProcessAccrualsAsync(2026, typeId);
        await using (var check = CreateContext())
        {
            var (sum, periods, count) = await AccrualSummaryAsync(check, empId, typeId);
            count.Should().Be(4);
            periods.Should().Equal(1, 2, 3, 4);
            sum.Should().Be(12m, "four quarters sum to the annual entitlement");
        }
    }

    // ── (4) Yearly: byte-identical to the pre-BUG-291 single full-entitlement row + label, re-run no-op ──────
    [Fact]
    public async Task Yearly_IsByteIdentical_SingleFullEntitlementRow_OnPostgres()
    {
        await using (var mig = CreateContext()) await mig.Database.MigrateAsync();

        Guid empId, typeId;
        await using (var seed = CreateContext())
        {
            var (deptId, jobId) = await SeedTenantScaffoldAsync(seed);
            var emp = NewEmployee(_tenantId, deptId, jobId, "PG-Y", new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            empId = emp.Id;
            seed.Employees.Add(emp);
            var lt = NewLeaveType(_tenantId, annual: 20m, AccrualFrequency.Yearly, "Yearly-20");
            typeId = lt.Id;
            seed.LeaveTypes.Add(lt);
            await seed.SaveChangesAsync();
        }

        var clock = new MutableClock(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        await using (var run = CreateContext())
            await BuildService(run, clock).ProcessAccrualsAsync(2026, typeId);
        // Re-run later in the year — must NOT duplicate (the byte-identical no-regression contract).
        clock.Now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var run = CreateContext())
            await BuildService(run, clock).ProcessAccrualsAsync(2026, typeId);

        await using (var check = CreateContext())
        {
            var rows = await check.LeaveLedgerEntries.AsNoTracking()
                .Where(l => l.EmployeeId == empId && l.LeaveTypeId == typeId
                            && l.EntryType == LedgerEntryType.Accrual)
                .ToListAsync();
            rows.Should().HaveCount(1, "Yearly resolves to a single period — one full-entitlement row, as before");
            rows[0].Amount.Should().Be(20m);
            rows[0].BalanceAfter.Should().Be(20m);
            rows[0].AccrualPeriod.Should().Be(1);
            rows[0].Description.Should().StartWith("Annual accrual",
                "the Yearly/Upfront label is byte-identical to the pre-BUG-291 accrual");
        }
    }

    // ── (5) Monthly + mid-year joiner + FTE<1: the two pro-rations COMPOSE (single discount), no double ─────
    [Fact]
    public async Task Monthly_MidYearJoiner_And_FteBelowOne_Compose_NoDoubleDiscount_OnPostgres()
    {
        await using (var mig = CreateContext()) await mig.Database.MigrateAsync();

        var joining = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc); // mid-year joiner
        const decimal fte = 0.5m;                                          // part-timer

        Guid empId, typeId;
        await using (var seed = CreateContext())
        {
            var (deptId, jobId) = await SeedTenantScaffoldAsync(seed);
            var emp = NewEmployee(_tenantId, deptId, jobId, "PG-MYJ", joining, fte);
            empId = emp.Id;
            seed.Employees.Add(emp);
            var lt = NewLeaveType(_tenantId, annual: 24m, AccrualFrequency.Monthly, "Monthly-24-MYJ");
            typeId = lt.Id;
            seed.LeaveTypes.Add(lt);
            await seed.SaveChangesAsync();
        }

        // The joiner+FTE pro-ration applied ONCE, via the same pure engine the service uses. A full year of
        // Monthly periods must sum to EXACTLY this — if the discount were applied twice (per-period AND on the
        // annual) the total would be strictly smaller.
        decimal proratedAnnual = LeaveEntitlementEngine.CalculateProRata(
            24m, joining, 2026, fiscalYearStartMonth: 1, fte: fte);
        proratedAnnual.Should().BeLessThan(24m, "a July joiner at 0.5 FTE is genuinely pro-rated (a real discount)");
        proratedAnnual.Should().BeGreaterThan(0m);

        var clock = new MutableClock(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        for (int month = 1; month <= 12; month++)
        {
            clock.Now = new DateTimeOffset(2026, month, 15, 0, 0, 0, TimeSpan.Zero);
            await using var run = CreateContext();
            await BuildService(run, clock).ProcessAccrualsAsync(2026, typeId);
        }

        await using (var check = CreateContext())
        {
            var (sum, _, count) = await AccrualSummaryAsync(check, empId, typeId);
            count.Should().Be(12);
            sum.Should().Be(proratedAnnual,
                "the joiner+FTE discount is applied ONCE (via proratedAnnual) and spread across periods — not double-discounted");
        }
    }
}
