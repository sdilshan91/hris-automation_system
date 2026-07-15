// ============================================================================
// CAL-5 / US-ATT-011 AC-4 / FR-5: holiday exclusion from the payroll working-days count, governed by the
// effective-dated TenantPayrollCalendarPolicy (ExcludeHolidaysFromWorkingDays, DEFAULT FALSE).
//
// ⚠ WHY THIS SUITE EXISTS — THE MONEY TRAP. `PayrollSlipCalculator.Compute` derives
//   proRataFactor = paidDaysBeforeLop / workingDays
// where the numerator is `PayrollRunProcessor.ProRataPaidDays` and the denominator is the working-days figure
// (attendance's TotalWorkingDays, else the run's own shift-days count). Both MUST be on the SAME basis. If
// holidays are subtracted from the DENOMINATOR ONLY, a mid-month joiner's numerator still counts holidays, the
// factor rises, and they are OVER-PAID — and the overshoot is INVISIBLE, because Compute silently clamps
// `paidDaysBeforeLop` to `workingDays`. `SingleBasis_...` below is the arm that fails on that mutation.
//
// The correct semantic is simply: a public holiday is NOT a working day — applied at every payroll site that
// counts working days (denominator AND numerator), on the employee's LOCATION-scoped holiday set.
//
// WHY POSTGRES: this asserts money figures out of the REAL PayrollRunProcessor.ProcessAsync over the real
// effective-dating query (OrderByDescending + date predicate), the real location-scoped IHolidayProvider, and
// the real migration DDL (including the exclude_holidays_from_working_days DEFAULT false). An InMemory version
// would not exercise the DDL default and would be measurably weaker on a money path.
//
// GOLDEN MONTH: September 2025 — Sep 1 is a Monday, so a Mon–Fri shift has exactly 22 working days.
// Holidays Sep 15 (Mon) + Sep 16 (Tue) → 20 working days when excluded.
//   BASIC 22,000/month:
//     flag OFF: OT hourly = 22000/(22*8) = 125.00 ; LOP daily = 22000/22 = 1000.00
//     flag ON : OT hourly = 22000/(20*8) = 137.50 ; LOP daily = 22000/20 = 1100.00
// ============================================================================

using System.Globalization;
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
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class PayrollWorkingDaysDenominatorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    // ── The golden period ───────────────────────────────────────────────
    private const int Year = 2025;
    private const int Month = 9;                                  // Sep 2025 — Sep 1 is a Monday.
    private const int ShiftWorkingDays = 22;                      // Mon–Fri days in Sep 2025.
    private static readonly DateOnly Holiday1 = new(2025, 9, 15); // Monday
    private static readonly DateOnly Holiday2 = new(2025, 9, 16); // Tuesday
    private const decimal Basic = 22000m;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(Guid.NewGuid());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ══════════════════════════════════════════════════════════════════════
    //  ARM 1 — CONTROL: no policy row ⇒ holidays change NOTHING (default FALSE)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TC-PAY-014: the whole point of the default-FALSE decision. With NO policy row (every existing tenant),
    /// a month containing two public holidays must produce a BYTE-IDENTICAL slip to a month with none —
    /// denominator, overtime amount and LOP amount alike. Had the flag defaulted to TRUE, every existing
    /// tenant's OT base and LOP rate would shift on their next run.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-014")]
    public async Task NoPolicyRow_HolidaysDoNotChangeAnyFigure_DefaultIsOff()
    {
        var withHolidays = Guid.NewGuid();
        var withoutHolidays = Guid.NewGuid();

        var empA = await SeedTenantAsync(withHolidays, holidays: [Holiday1, Holiday2], policyExcludeHolidays: null);
        var empB = await SeedTenantAsync(withoutHolidays, holidays: [], policyExcludeHolidays: null);

        await SeedAttendanceAsync(withHolidays, empA, lopDays: 2m, otMinutes: 120, multiplier: 2m);
        await SeedAttendanceAsync(withoutHolidays, empB, lopDays: 2m, otMinutes: 120, multiplier: 2m);

        var slipA = await RunAsync(withHolidays);
        var slipB = await RunAsync(withoutHolidays);

        // The holiday month must look exactly like the holiday-free month.
        slipA.WorkingDays.Should().Be(slipB.WorkingDays, "no policy row ⇒ holidays are still working days");
        slipA.OvertimeAmount.Should().Be(slipB.OvertimeAmount, "the OT hourly base must not move");
        slipA.GrossEarnings.Should().Be(slipB.GrossEarnings);
        slipA.TotalDeductions.Should().Be(slipB.TotalDeductions, "the LOP daily rate must not move");
        slipA.NetSalary.Should().Be(slipB.NetSalary);

        // ...and that identical figure is the pre-CAL-5 one, pinned absolutely (not just "equal to each other").
        slipA.WorkingDays.Should().Be(22m);
        slipA.OvertimeAmount.Should().Be(500.00m);      // 2h * 125.00 * 2
        (await LopAmountAsync(withHolidays, slipA.Id)).Should().Be(2000.00m);  // 1000.00/day * 2
        slipA.NetSalary.Should().Be(20500.00m);         // 22000 + 500 - 2000
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ARMS 2/3/4 — flag ON: denominator drops, OT base rises, LOP costs more
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TC-PAY-015 (AC-4): with the policy in effect, an employee whose location observes two public holidays in
    /// the period has a working-days denominator of 22 − 2 = 20.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-015")]
    public async Task FlagOn_DenominatorExcludesHolidays()
    {
        var tenantId = Guid.NewGuid();
        var empId = await SeedTenantAsync(tenantId, holidays: [Holiday1, Holiday2], policyExcludeHolidays: true);
        await SeedAttendanceAsync(tenantId, empId, lopDays: 0m, otMinutes: 0, multiplier: 2m);

        var slip = await RunAsync(tenantId);

        slip.WorkingDays.Should().Be(ShiftWorkingDays - 2, "a public holiday is not a working day");
    }

    /// <summary>
    /// TC-PAY-015: excluding holidays shrinks the working-days divisor, so the SAME approved overtime minutes
    /// earn MORE — hourly = basic / (working_days * 8) rises 125.00 → 137.50, so 2h @ 2x goes 500 → 550.
    /// Exact figures, not a ">" comparison.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-015")]
    public async Task FlagOn_OvertimeHourlyBaseRises_SameMinutesEarnMore()
    {
        var offTenant = Guid.NewGuid();
        var onTenant = Guid.NewGuid();

        var empOff = await SeedTenantAsync(offTenant, holidays: [Holiday1, Holiday2], policyExcludeHolidays: false);
        var empOn = await SeedTenantAsync(onTenant, holidays: [Holiday1, Holiday2], policyExcludeHolidays: true);

        await SeedAttendanceAsync(offTenant, empOff, lopDays: 0m, otMinutes: 120, multiplier: 2m);
        await SeedAttendanceAsync(onTenant, empOn, lopDays: 0m, otMinutes: 120, multiplier: 2m);

        var slipOff = await RunAsync(offTenant);
        var slipOn = await RunAsync(onTenant);

        slipOff.WorkingDays.Should().Be(22m);
        slipOff.OvertimeAmount.Should().Be(500.00m);   // 2h * (22000/(22*8) = 125.00) * 2
        slipOn.WorkingDays.Should().Be(20m);
        slipOn.OvertimeAmount.Should().Be(550.00m);    // 2h * (22000/(20*8) = 137.50) * 2

        slipOn.OvertimeAmount.Should().BeGreaterThan(slipOff.OvertimeAmount,
            "fewer working days ⇒ a higher hourly base for the same salary");
    }

    /// <summary>
    /// TC-PAY-015 — named honestly: excluding holidays makes LOP cost the employee MORE. The daily rate is
    /// basic / working_days, so it rises 1000.00 → 1100.00 and the same 2 LOP days deduct 2000 → 2200. This is a
    /// real consequence of the flag and the reason it must be opted into on a tenant-chosen effective date.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-015")]
    public async Task FlagOn_LopDailyRateRises_SameLopDaysDeductMore()
    {
        var offTenant = Guid.NewGuid();
        var onTenant = Guid.NewGuid();

        var empOff = await SeedTenantAsync(offTenant, holidays: [Holiday1, Holiday2], policyExcludeHolidays: false);
        var empOn = await SeedTenantAsync(onTenant, holidays: [Holiday1, Holiday2], policyExcludeHolidays: true);

        await SeedAttendanceAsync(offTenant, empOff, lopDays: 2m, otMinutes: 0, multiplier: 2m);
        await SeedAttendanceAsync(onTenant, empOn, lopDays: 2m, otMinutes: 0, multiplier: 2m);

        var slipOff = await RunAsync(offTenant);
        var slipOn = await RunAsync(onTenant);

        var lopOff = await LopAmountAsync(offTenant, slipOff.Id);
        var lopOn = await LopAmountAsync(onTenant, slipOn.Id);

        lopOff.Should().Be(2000.00m);   // (22000/22 = 1000.00)/day * 2
        lopOn.Should().Be(2200.00m);    // (22000/20 = 1100.00)/day * 2
        lopOn.Should().BeGreaterThan(lopOff, "excluding holidays raises the LOP daily rate — LOP costs MORE");

        slipOn.NetSalary.Should().Be(19800.00m);   // 22000 - 2200
        slipOff.NetSalary.Should().Be(20000.00m);  // 22000 - 2000
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ARM 5 — THE MONEY TRAP: single-basis pro-ration
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TC-PAY-015 (money-critical). A mid-month joiner (DOJ Sep 16) with the flag ON must be pro-rated on
    /// holiday-excluded days on BOTH sides:
    ///   denominator = 22 − 2 (Sep 15, 16)        = 20
    ///   numerator   = Mon–Fri in [Sep 16..Sep 30] = 11, minus the Sep 16 holiday = 10
    ///   factor      = 10/20 = 0.5 → gross = 22000 * 0.5 = 11,000.00
    ///
    /// <para>⚠ THIS ARM IS THE TRAP DETECTOR. If holidays are threaded into the DENOMINATOR but not into
    /// <c>ProRataPaidDays</c>, the numerator stays 11 → factor 11/20 = 0.55 → gross 12,100.00 — the joiner is
    /// OVER-PAID by 1,100. Verified by mutation: removing the holiday argument from the ProRataPaidDays call
    /// site turns this arm RED.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-015")]
    public async Task FlagOn_SingleBasisHeld_JoinerProRationUsesHolidayExcludedDaysOnBothSides()
    {
        var tenantId = Guid.NewGuid();
        var empId = await SeedTenantAsync(
            tenantId,
            holidays: [Holiday1, Holiday2],
            policyExcludeHolidays: true,
            dateOfJoining: new DateTime(2025, 9, 16));   // joins ON a holiday, mid-month.

        // No attendance row / unlocked period ⇒ the denominator comes from the run's own shift-days count,
        // which is the site the numerator must agree with.
        var slip = await RunAsync(tenantId);

        slip.WorkingDays.Should().Be(20m, "denominator = 22 shift days − 2 holidays");
        slip.PaidDays.Should().Be(10m, "numerator = 11 employed shift days − the Sep 16 holiday");
        slip.GrossEarnings.Should().Be(11000.00m,
            "factor must be 10/20 = 0.5 — NOT 11/20 = 0.55, which would over-pay the joiner by 1,100");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ARM 6 — holidays are LOCATION-scoped
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TC-PAY-015 (BR-2): a holiday declared for Location A must not shrink a Location-B employee's working
    /// days. Both employees are in the same tenant and the same run, so a tenant-wide holiday lookup would fail
    /// this arm.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-015")]
    public async Task FlagOn_HolidayAtOneLocation_DoesNotReduceAnotherLocationsDenominator()
    {
        var tenantId = Guid.NewGuid();
        Guid empA, empB;

        await using (var seed = Db(tenantId))
        {
            SeedShift(seed, tenantId);
            var locationA = NewLocation(tenantId, "Dubai");
            var locationB = NewLocation(tenantId, "Colombo");
            seed.Locations.AddRange(locationA, locationB);

            empA = SeedEmployee(seed, tenantId, "DXB1", locationA.Id, new DateTime(2020, 1, 1));
            empB = SeedEmployee(seed, tenantId, "CMB1", locationB.Id, new DateTime(2020, 1, 1));
            // Without a BASIC component the run produces NO slip for the employee at all. ONE component per
            // tenant (ix_salary_component_tenant_id_code is unique on tenant+code) linked to BOTH employees.
            SeedSalary(seed, tenantId, empA, empB);

            // A holiday scoped to Location A ONLY.
            seed.Holidays.Add(new Holiday
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Dubai Only",
                Date = Holiday1, Type = HolidayType.Public, LocationId = locationA.Id, IsActive = true,
            });

            seed.TenantPayrollCalendarPolicies.Add(new TenantPayrollCalendarPolicy
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
                EffectiveFrom = new DateOnly(2025, 1, 1),
                ExcludeHolidaysFromWorkingDays = true, IsActive = true,
            });

            await seed.SaveChangesAsync();
        }

        await RunOnlyAsync(tenantId);

        await using var db = Db(tenantId);
        var slips = await db.PayrollSlips.AsNoTracking().ToListAsync();

        slips.Single(s => s.EmployeeId == empA).WorkingDays.Should().Be(21m,
            "the Dubai employee loses the Dubai holiday");
        slips.Single(s => s.EmployeeId == empB).WorkingDays.Should().Be(22m,
            "a Dubai holiday must NOT shrink a Colombo employee's working days");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ARMS 7/8 — effective-dating
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TC-PAY-015 (AC-4): policy changes are never retroactive. v1 (flag OFF) effective Jan 2025, v2 (flag ON)
    /// effective Jun 2025. A MAY period resolves v1 → holidays still count; a JULY period resolves v2 → they
    /// don't. This is what lets a tenant opt in from a date of their choosing without rewriting earlier months.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-015")]
    public async Task EffectiveDating_MayResolvesTheOffVersion_JulyResolvesTheOnVersion()
    {
        var tenantId = Guid.NewGuid();
        Guid empId;

        await using (var seed = Db(tenantId))
        {
            SeedShift(seed, tenantId);
            empId = SeedEmployee(seed, tenantId, "E1", locationId: null, doj: new DateTime(2020, 1, 1));
            SeedSalary(seed, tenantId, empId);

            // Two tenant-wide holidays in MAY 2025 (Thu 1st, Fri 2nd) and two in JULY 2025 (Tue 1st, Wed 2nd).
            foreach (var d in new[] { new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 2), new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 2) })
                seed.Holidays.Add(new Holiday
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = $"H{d:MMdd}",
                    Date = d, Type = HolidayType.Public, LocationId = null, IsActive = true,
                });

            seed.TenantPayrollCalendarPolicies.AddRange(
                new TenantPayrollCalendarPolicy
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
                    EffectiveFrom = new DateOnly(2025, 1, 1),
                    ExcludeHolidaysFromWorkingDays = false, IsActive = true,   // v1
                },
                new TenantPayrollCalendarPolicy
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
                    EffectiveFrom = new DateOnly(2025, 6, 1),
                    ExcludeHolidaysFromWorkingDays = true, IsActive = true,    // v2
                });

            await seed.SaveChangesAsync();
        }

        var maySlip = await RunAsync(tenantId, 2025, 5);
        maySlip.WorkingDays.Should().Be(22m,
            "May resolves v1 (flag off) — the June change must NOT reach back into May");

        var julySlip = await RunAsync(tenantId, 2025, 7);
        julySlip.WorkingDays.Should().Be(21m,
            "July resolves v2 (flag on) — 23 Mon–Fri days in Jul 2025 minus 2 holidays");
    }

    /// <summary>
    /// TC-PAY-015: re-configuring the SAME effective date REPLACES that version rather than adding a second one
    /// — otherwise resolution would tie-break between two versions on one date non-deterministically, and an
    /// ambiguous tie-break here is an ambiguous OT base.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-015")]
    public async Task SameEffectiveDateReconfig_ReplacesThePriorVersion()
    {
        var tenantId = Guid.NewGuid();
        var effectiveFrom = new DateOnly(2025, 9, 1);

        await using var provider = BuildProvider(tenantId);
        var mediator = provider.GetRequiredService<IMediator>();

        var first = await mediator.Send(new CreatePayrollCalendarPolicyCommand(effectiveFrom, true, true));
        first.IsSuccess.Should().BeTrue(first.Error);

        var second = await mediator.Send(new CreatePayrollCalendarPolicyCommand(effectiveFrom, false, true));
        second.IsSuccess.Should().BeTrue(second.Error);

        var service = provider.GetRequiredService<IPayrollCalendarPolicyService>();

        var list = await service.ListAsync();
        list.Value!.Should().ContainSingle("one active version per effective date")
            .Which.ExcludeHolidaysFromWorkingDays.Should().BeFalse("the later configuration wins");

        var effective = await service.GetEffectiveAsync(effectiveFrom);
        effective.Value!.ExcludeHolidaysFromWorkingDays.Should().BeFalse();
        effective.Value.IsDefault.Should().BeFalse("a real row is configured");
    }

    /// <summary>
    /// TC-PAY-014: with no row configured, the API reports the code-default — holidays NOT excluded — rather
    /// than implying the behaviour. The engine and this endpoint share one resolution path.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-014")]
    public async Task NoPolicyConfigured_GetEffective_ReportsCodeDefaultOff()
    {
        var tenantId = Guid.NewGuid();
        await using var provider = BuildProvider(tenantId);

        var effective = await provider.GetRequiredService<IPayrollCalendarPolicyService>()
            .GetEffectiveAsync(new DateOnly(2025, 9, 1));

        effective.Value!.IsDefault.Should().BeTrue();
        effective.Value.ExcludeHolidaysFromWorkingDays.Should().BeFalse(
            "the code-default must never silently opt a tenant into a money-affecting change");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Harness
    // ══════════════════════════════════════════════════════════════════════

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; init; }
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
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; init; } = Guid.NewGuid();
        public string Email => "hr@acme.test";
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

    private sealed class InMemoryExportStorage : IReportExportStorage
    {
        public Task<string> SaveAsync(Guid tenantId, Guid reportId, string fileName,
            string contentType, byte[] content, CancellationToken cancellationToken = default)
            => Task.FromResult($"mem://{tenantId}/{reportId}/{fileName}");
    }

    /// <summary>
    /// UseSnakeCaseNamingConvention() is NOT optional — omitting it makes MigrateAsync throw
    /// PendingModelChangesWarning. EnableRetryOnFailure mirrors production (and is what makes RLS-adjacent
    /// behaviour reproduce).
    /// </summary>
    private AppDbContext Db(Guid tenantId)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        return new AppDbContext(DbOptions(tc, tenantId), tc);
    }

    private DbContextOptions<AppDbContext> DbOptions(ITenantContext tc, Guid tenantId)
        => new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                new TenantInterceptor(tc),
                new AuditInterceptor(new FakeCurrentUser { TenantId = tenantId }))
            .Options;

    /// <summary>The payroll run's DI graph — mirrors MultiCountryTaxFoundationTests.Provider, on real Postgres.</summary>
    private ServiceProvider BuildProvider(Guid tenantId)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tc);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = tenantId });
        services.AddSingleton<IPayrollNotificationService, LogOnlyPayrollNotificationService>();
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            });
            o.UseSnakeCaseNamingConvention();
            o.AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(new FakeCurrentUser { TenantId = tenantId }));
        });
        services.AddScoped<IReportExportStorage, InMemoryExportStorage>();
        services.AddScoped<IOvertimeService, OvertimeService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IAttendanceSummaryService, AttendanceSummaryService>();
        services.AddScoped<IAttendancePayrollService, AttendancePayrollService>();
        services.AddScoped<IStatutoryDeductionResolver, StatutoryDeductionResolver>();
        services.AddScoped<IPayrollAdjustmentResolver, PayrollAdjustmentResolver>();
        services.AddScoped<IPayrollAuditLogger, PayrollAuditLogger>();
        services.AddScoped<IPayrollSlipCleaner, PayrollSlipCleaner>();
        services.AddScoped<IPayrollRunProcessor, PayrollRunProcessor>();
        // CAL-5: the location-aware holiday source + the effective-dated calendar policy CRUD.
        services.AddScoped<IHolidayProvider, HolidayProvider>();
        services.AddScoped<IPayrollCalendarPolicyService, PayrollCalendarPolicyService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreatePayrollCalendarPolicyCommand).Assembly));
        return services.BuildServiceProvider();
    }

    // ── seeding ────────────────────────────────────────────────────────

    private static Location NewLocation(Guid tenantId, string name) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = name, TimeZone = "UTC", IsActive = true,
    };

    /// <summary>The tenant default Mon–Fri shift — the basis every working-days count in this suite resolves to.</summary>
    private static void SeedShift(AppDbContext db, Guid tenantId) => db.Shifts.Add(new Shift
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "General", Type = ShiftType.Single,
        StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0),
        BreakDurationMinutes = 0, GracePeriodMinutes = 15,
        WorkingDays = new List<int> { 1, 2, 3, 4, 5 },
        IsDefault = true, IsActive = true,
    });

    private static Guid SeedEmployee(AppDbContext db, Guid tenantId, string no, Guid? locationId, DateTime doj)
    {
        var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = $"D{no}", Code = no };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, TitleName = $"T{no}" };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);

        var id = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = id, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "W",
            Email = $"{no}@{tenantId:N}.test", DepartmentId = dept.Id, JobTitleId = title.Id,
            DateOfJoining = doj, EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active, IsActive = true, LocationId = locationId,
        });
        return id;
    }

    /// <summary>
    /// A single BASIC earning of 22,000/month for each supplied employee, current as-of today (the run resolves
    /// components at UtcNow). ONE SalaryComponent per tenant — `ix_salary_component_tenant_id_code` is unique on
    /// (tenant, code), so a second "BASIC" for the same tenant is a 23505.
    /// </summary>
    private static void SeedSalary(AppDbContext db, Guid tenantId, params Guid[] employeeIds)
    {
        var component = new SalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Basic Salary", Code = "BASIC",
            Type = SalaryComponentType.Earning, CalculationMethod = CalculationMethod.Fixed,
            IsTaxable = true, IsStatutory = false, IsActive = true, ProcessingOrder = 1,
        };
        db.SalaryComponents.Add(component);

        foreach (var employeeId in employeeIds)
        {
            db.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
                SalaryStructureId = BaseEntity.NewUuidV7(), SalaryComponentId = component.Id,
                AnnualAmount = Basic * 12m, MonthlyAmount = Basic, IsOverride = false,
                EffectiveFrom = new DateOnly(2020, 1, 1), EffectiveTo = null,
            });
        }
    }

    /// <summary>Seeds shift + employee + salary (+ tenant-wide holidays, + an optional policy row) and returns the employee id.</summary>
    private async Task<Guid> SeedTenantAsync(
        Guid tenantId,
        IReadOnlyList<DateOnly> holidays,
        bool? policyExcludeHolidays,
        DateTime? dateOfJoining = null)
    {
        await using var db = Db(tenantId);
        SeedShift(db, tenantId);
        var empId = SeedEmployee(db, tenantId, "E1", locationId: null, doj: dateOfJoining ?? new DateTime(2020, 1, 1));
        SeedSalary(db, tenantId, empId);

        foreach (var d in holidays)
            db.Holidays.Add(new Holiday
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = $"H{d:MMdd}",
                Date = d, Type = HolidayType.Public, LocationId = null, IsActive = true,
            });

        // null ⇒ NO policy row at all (the control: every existing tenant).
        if (policyExcludeHolidays is { } exclude)
            db.TenantPayrollCalendarPolicies.Add(new TenantPayrollCalendarPolicy
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
                EffectiveFrom = new DateOnly(2025, 1, 1),
                ExcludeHolidaysFromWorkingDays = exclude, IsActive = true,
            });

        await db.SaveChangesAsync();
        return empId;
    }

    /// <summary>
    /// Locks the period and gives the employee real attendance so the run applies LOP + overtime:
    /// a materialized monthly summary (served verbatim by GetMonthlyAsync, so LopDays is deterministic) and an
    /// APPROVED overtime record (which also puts the employee in the ISSUE-090 "has real records" set).
    /// </summary>
    private async Task SeedAttendanceAsync(Guid tenantId, Guid employeeId, decimal lopDays, int otMinutes, decimal multiplier)
    {
        await using var db = Db(tenantId);

        var monthStart = new DateOnly(Year, Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        db.AttendancePeriodLocks.Add(new AttendancePeriodLock
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
            PeriodStart = monthStart, PeriodEnd = monthEnd,
            IsLocked = true, LockedAt = DateTime.UtcNow, LockedBy = Guid.NewGuid(),
        });

        db.AttendanceMonthlySummaries.Add(new AttendanceMonthlySummary
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            YearMonth = $"{Year:D4}-{Month:D2}",
            TotalPresentDays = ShiftWorkingDays - lopDays,
            TotalAbsentDays = lopDays,
            LopDays = lopDays,
            TotalWorkMinutes = 0, TotalOvertimeMinutes = otMinutes,
            GeneratedAt = DateTime.UtcNow,
        });

        // ISSUE-090: AttendancePayrollService OMITS an employee with NO attendance data for the period
        // ("no data" is not "absent") — it unions AttendanceLogs / approved leave / regularizations /
        // approved overtime. Without one of those, the summary row above is ignored, `attendance` is null,
        // and LopDays/OvertimeMinutes never reach the slip. Seed a real clock-in so this employee HAS data:
        // an employee with LOP days who never clocked in anywhere is not a state payroll should price.
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            ClockIn = new DateTime(Year, Month, 10, 9, 0, 0, DateTimeKind.Utc),
            ClockOut = new DateTime(Year, Month, 10, 17, 0, 0, DateTimeKind.Utc),
            TotalWorkMinutes = 480,
        });

        if (otMinutes > 0)
            db.OvertimeRecords.Add(new OvertimeRecord
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
                Date = new DateOnly(Year, Month, 10),   // a plain working day, not one of the holidays.
                OvertimeMinutes = otMinutes, ApprovedMinutes = otMinutes,
                Multiplier = multiplier, Type = OvertimeType.PreApproved, Status = OvertimeStatus.Approved,
            });

        await db.SaveChangesAsync();
    }

    // ── running ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a run for the period and processes it through the REAL PayrollRunProcessor. Returns the run id
    /// so the caller can read whichever slips it cares about — arms with more than one employee cannot use
    /// RunAsync, whose SingleAsync would throw.
    /// </summary>
    private async Task<Guid> RunCoreAsync(Guid tenantId, int year, int month)
    {
        Guid runId;
        await using (var db = Db(tenantId))
        {
            runId = BaseEntity.NewUuidV7();
            db.PayrollRuns.Add(new PayrollRun
            {
                Id = runId, TenantId = tenantId, PayYear = year, PayMonth = month,
                Status = PayrollRunStatus.Queued, InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var provider = BuildProvider(tenantId))
        {
            var result = await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(runId);
            result.IsSuccess.Should().BeTrue(result.Error);
        }

        return runId;
    }

    /// <summary>Runs payroll for a multi-employee tenant; the caller reads the slips itself.</summary>
    private Task RunOnlyAsync(Guid tenantId, int year = Year, int month = Month)
        => RunCoreAsync(tenantId, year, month);

    /// <summary>Runs payroll for a SINGLE-employee tenant and returns that employee's slip.</summary>
    private async Task<PayrollSlip> RunAsync(Guid tenantId, int year = Year, int month = Month)
    {
        var runId = await RunCoreAsync(tenantId, year, month);
        await using var read = Db(tenantId);
        return await read.PayrollSlips.AsNoTracking().SingleAsync(s => s.PayrollRunId == runId);
    }

    /// <summary>The LOP deduction amount on a slip (0 when there is no LOP line).</summary>
    private async Task<decimal> LopAmountAsync(Guid tenantId, Guid slipId)
    {
        await using var db = Db(tenantId);
        var line = await db.PayrollSlipDetails.AsNoTracking()
            .Where(d => d.PayrollSlipId == slipId && d.ComponentName == "Loss of Pay")
            .Select(d => (decimal?)d.Amount)
            .FirstOrDefaultAsync();
        return line ?? 0m;
    }
}
