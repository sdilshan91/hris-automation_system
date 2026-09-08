// ============================================================================
// BUG-456 — the PLUMBING of the tenant's configured weekday overtime multiplier (US-PAY-010 FR-4,
// US-ATT-006 BR-3).
//
// ⚠ WHY THIS SUITE EXISTS. `PayrollOvertimeCalculator.Compute` has taken a `defaultMultiplier` since
// US-PAY-010, and it defaults to 1.5. `PayrollRunProcessor.ComputeOvertime` never supplied it, so the
// persisted, API-settable, UI-settable `AttendanceSettings.WeekdayOvertimeMultiplier` was inert on the
// payroll path: a tenant could save 2.0x, see it persist, and be paid 1.5x on every run. Nothing went
// red — omitting an optional argument compiles, and NO test anywhere passed `defaultMultiplier`, so
// the parameter was never exercised at all (`grep defaultMultiplier` returned only the declaration and
// the ARCH-004 baseline entry that recorded it as an open finding).
//
// ── WHAT `defaultMultiplier` ACTUALLY PRICES, and why the arms below look the way they do ──
// It is the fallback, NOT the rate. `Compute` reads the per-multiplier breakdown first
// (`OvertimeMultiplierDetails`, keyed by the rate as a string) and pays each bucket at ITS OWN key
// whenever that key parses to a positive decimal. `defaultMultiplier` applies in exactly two places:
//   (a) the breakdown is empty while approved minutes are positive — the legacy-attendance shape; and
//   (b) a bucket key that is NOT a positive decimal (`ParseMultiplier`'s `m > 0m` guard).
// Through the live pull (`AttendancePayrollService`) shape (a) is unreachable: the minutes total and
// the breakdown are projected from the SAME `OvertimeAgg`, so an empty breakdown implies zero minutes.
// Shape (b) IS reachable, and it is what these arms drive: an APPROVED `OvertimeRecord` whose
// `Multiplier` is 0 — not producible by today's two creation paths (both resolve the rate through
// `OvertimeMultiplierResolver` off validated settings), but entirely producible by an import, a
// back-fill, or a row that predates that resolver. That row currently pays a hardcoded 1.5x; after the
// fix it pays the tenant's configured weekday rate, which is the only defensible reading of "this
// overtime carries no rate of its own".
//
// So every arm goes through the REAL PayrollRunProcessor.ProcessAsync and asserts MONEY off the
// PERSISTED slip. A calculator-level unit test cannot close this gap — the calculator was always
// correct; the call site was not.
//
// WHY POSTGRES: this asserts money out of the real run over the real migration DDL, including
// `attendance_settings.weekday_overtime_multiplier numeric(3,2) DEFAULT 1.5` — the very default the
// "unconfigured tenant" arm depends on. InMemory exercises neither.
//
// GOLDEN MONTH: September 2025 — Sep 1 is a Monday, so a Mon–Fri shift has exactly 22 working days.
//   BASIC 22,000/month, 2h of approved OT, hourly = 22000/(22*8) = 125.00:
//     @ 1.50x (the code default) → 2 * 125.00 * 1.5 =   375.00
//     @ 2.00x (tenant-configured) → 2 * 125.00 * 2.0 =   500.00
//     @ 3.00x (a location override, or a per-record rate) → 2 * 125.00 * 3.0 = 750.00
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
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class PayrollWeekdayOvertimeMultiplierTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private const int Year = 2025;
    private const int Month = 9;               // Sep 2025 — Sep 1 is a Monday.
    private const int ShiftWorkingDays = 22;   // Mon–Fri days in Sep 2025.
    private const decimal Basic = 22000m;
    private const int OtMinutes = 120;         // 2 hours.

    /// <summary>A record carrying NO usable rate of its own — the shape `defaultMultiplier` exists to price.</summary>
    private const decimal NoUsableRecordMultiplier = 0m;

    private const decimal CodeDefaultOtAmount = 375.00m;   // 2h * 125.00 * 1.5x
    private const decimal TwoXOtAmount = 500.00m;          // 2h * 125.00 * 2.0x
    private const decimal ThreeXOtAmount = 750.00m;        // 2h * 125.00 * 3.0x

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(Guid.NewGuid());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ══════════════════════════════════════════════════════════════════════
    //  ARM 1 — the defect: a tenant configured at 2.0x, through the REAL run
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TC-PAY-456 (BUG-456): a tenant whose attendance policy sets <c>WeekdayOvertimeMultiplier = 2.00</c> must
    /// have overtime that carries no per-record rate paid at 2.0x — 500.00, not 375.00.
    ///
    /// <para>This is the arm that FAILS before the fix, with the wrong MONEY figure: the processor omitted
    /// <c>defaultMultiplier</c>, so the calculator's own 1.5 default applied and the slip carried 375.00 —
    /// a 25% under-payment of that overtime, on every run, while the settings screen showed 2.0.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-456")]
    public async Task TenantConfiguredAtTwoX_OvertimeWithNoPerRecordRate_IsPaidAtTwoX_ThroughTheRealRun()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = await SeedTenantAsync(tenantId, weekdayMultiplier: 2.0m);
        await SeedAttendanceAsync(tenantId, employeeId, NoUsableRecordMultiplier);

        var slips = await RunAsync(tenantId);

        slips[employeeId].OvertimeAmount.Should().Be(
            TwoXOtAmount,
            "the tenant configured 2.0x and this overtime carries no rate of its own, so 2h at a 125.00 base " +
            "is worth 500.00 — 375.00 would mean the run silently used the calculator's hardcoded 1.5x " +
            "instead of the tenant's saved policy (BUG-456)");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ARM 2 — CONTROL: an unconfigured tenant keeps the documented default
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TC-PAY-456 (control): a tenant with NO attendance-settings row at all — the state most tenants are
    /// actually in — must still be priced at the documented 1.5x code default, and the run must not create a
    /// policy row as a side effect.
    ///
    /// <para>The count assertion is not decoration: it distinguishes the batched, read-only
    /// <c>AttendancePolicyResolver.LoadAllAsync</c>/<c>For</c> pair from
    /// <c>ResolveForEmployeeAsync</c>, which lazily CREATES the tenant-default row. Resolving the multiplier
    /// through the latter would make a payroll run write attendance policy, and would silently pin the tenant
    /// to code defaults it never chose.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-456")]
    public async Task NoSettingsRow_OvertimeKeepsTheDocumentedCodeDefault_AndTheRunWritesNoPolicy()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = await SeedTenantAsync(tenantId, weekdayMultiplier: null);
        await SeedAttendanceAsync(tenantId, employeeId, NoUsableRecordMultiplier);

        var slips = await RunAsync(tenantId);

        slips[employeeId].OvertimeAmount.Should().Be(
            CodeDefaultOtAmount,
            "no policy row ⇒ the documented 1.5x default; threading the tenant multiplier through must not " +
            "move a single unconfigured tenant's money");

        await using var db = Db(tenantId);
        (await db.AttendanceSettings.AsNoTracking().CountAsync()).Should().Be(
            0, "a payroll run must never write attendance POLICY as a side effect");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ARM 3 — PRECEDENCE: a per-record rate beats the tenant default, both ways
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TC-PAY-456 (precedence, US-ATT-009 BR-5): <c>OvertimeMultiplierDetails</c> outranks the tenant default.
    /// ONE tenant at 2.0x, ONE run, two employees whose overtime carries an explicit rate — one ABOVE the
    /// tenant default (3.0x, e.g. a holiday block) and one BELOW it (1.5x, a plain weekday block saved before
    /// the tenant raised its rate). Each must be paid at its OWN rate, never at 2.0x.
    ///
    /// <para>Both directions are asserted deliberately. Inverting the precedence — letting the tenant default
    /// win — would OVER-pay the 1.5x record and UNDER-pay the 3.0x one, and a single-direction arm would catch
    /// only one of those. This is the arm that pins the already-correct modern path as still correct.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-456")]
    public async Task ExplicitPerRecordMultipliers_BeatTheTenantDefault_AboveAndBelowIt()
    {
        var tenantId = Guid.NewGuid();
        var aboveDefault = await SeedTenantAsync(tenantId, weekdayMultiplier: 2.0m);
        var belowDefault = await AddEmployeeAsync(tenantId, "E2");

        await SeedAttendanceAsync(tenantId, aboveDefault, recordMultiplier: 3.0m);
        await SeedAttendanceAsync(tenantId, belowDefault, recordMultiplier: 1.5m);

        var slips = await RunAsync(tenantId);

        slips[aboveDefault].OvertimeAmount.Should().Be(
            ThreeXOtAmount,
            "the record carries an explicit 3.0x, so it is paid at 3.0x — 500.00 would mean the tenant's " +
            "2.0x default overrode a record that already had a rate, UNDER-paying it");
        slips[belowDefault].OvertimeAmount.Should().Be(
            CodeDefaultOtAmount,
            "the record carries an explicit 1.5x, so it is paid at 1.5x — 500.00 would mean the tenant's " +
            "2.0x default overrode a record that already had a rate, OVER-paying it");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ARM 4 — the multiplier is resolved per LOCATION, not tenant-wide
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TC-PAY-456 (US-ATT-011 AC-3): the multiplier is read off the employee's EFFECTIVE policy — their
    /// Location's override wins wholesale over the tenant default — and off the SAME resolved row as
    /// <c>FteScaledOvertimeBase</c>. A branch on 3.0x and a head-office employee on the tenant's 2.0x, in ONE
    /// run, must be paid differently.
    ///
    /// <para>Distinguishes "resolved per employee" from "read one arbitrary settings row", which is the failure
    /// mode <c>AttendancePolicyResolver</c> exists to prevent: an unpredicated read would apply one branch's
    /// overtime rate to the whole tenant's payroll.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-456")]
    public async Task LocationOverride_PricesOnlyThatLocationsEmployee_NotTheWholeTenant()
    {
        var tenantId = Guid.NewGuid();
        var headOffice = await SeedTenantAsync(tenantId, weekdayMultiplier: 2.0m);

        Guid branchWorker;
        await using (var db = Db(tenantId))
        {
            var location = new Location
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Branch", TimeZone = "UTC", IsActive = true,
            };
            db.Locations.Add(location);

            branchWorker = SeedEmployee(db, tenantId, "E3", location.Id);
            AddSalary(db, tenantId, branchWorker);

            db.AttendanceSettings.Add(new AttendanceSettings
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
                LocationId = location.Id, WeekdayOvertimeMultiplier = 3.0m,
            });
            await db.SaveChangesAsync();
        }

        await SeedAttendanceAsync(tenantId, headOffice, NoUsableRecordMultiplier);
        await SeedAttendanceAsync(tenantId, branchWorker, NoUsableRecordMultiplier);

        var slips = await RunAsync(tenantId);

        slips[branchWorker].OvertimeAmount.Should().Be(
            ThreeXOtAmount, "the Location override (3.0x) wins wholesale over the tenant default (2.0x)");
        slips[headOffice].OvertimeAmount.Should().Be(
            TwoXOtAmount, "an employee with no Location falls back to the tenant default (2.0x), not the branch's");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Harness — mirrors PayrollFteOvertimeBaseTests (same run, same DI graph)
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
    /// PendingModelChangesWarning.
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

    /// <summary>The payroll run's DI graph — mirrors PayrollFteOvertimeBaseTests.BuildProvider.</summary>
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
        services.AddScoped<IHolidayProvider, HolidayProvider>();
        services.AddScoped<IPayrollCalendarPolicyService, PayrollCalendarPolicyService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreatePayrollCalendarPolicyCommand).Assembly));
        return services.BuildServiceProvider();
    }

    // ── seeding ────────────────────────────────────────────────────────

    /// <summary>The tenant default Mon–Fri shift — the basis the 22 working-days count resolves to.</summary>
    private static void SeedShift(AppDbContext db, Guid tenantId) => db.Shifts.Add(new Shift
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "General", Type = ShiftType.Single,
        StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0),
        BreakDurationMinutes = 0, GracePeriodMinutes = 15,
        WorkingDays = new List<int> { 1, 2, 3, 4, 5 },
        IsDefault = true, IsActive = true,
    });

    /// <summary>
    /// A full-time (1.0 FTE) employee. FTE is deliberately never varied in this suite: the FTE-scaled base is a
    /// SEPARATE policy with its own suite (PayrollFteOvertimeBaseTests), and holding it at the default keeps
    /// every figure here a function of the multiplier alone.
    /// </summary>
    private static Guid SeedEmployee(AppDbContext db, Guid tenantId, string no, Guid? locationId = null)
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
            DateOfJoining = new DateTime(2020, 1, 1), EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active, IsActive = true, LocationId = locationId,
            Fte = 1.00m,
        });
        return id;
    }

    /// <summary>Assigns the tenant's existing BASIC component to one employee (22,000/month, no end date).</summary>
    private static void AddSalary(AppDbContext db, Guid tenantId, Guid employeeId)
    {
        var componentId = db.SalaryComponents.Local.FirstOrDefault(c => c.Code == "BASIC")?.Id
            ?? db.SalaryComponents.AsNoTracking().Single(c => c.Code == "BASIC").Id;

        db.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            SalaryStructureId = BaseEntity.NewUuidV7(), SalaryComponentId = componentId,
            AnnualAmount = Basic * 12m, MonthlyAmount = Basic, IsOverride = false,
            EffectiveFrom = new DateOnly(2020, 1, 1), EffectiveTo = null,
        });
    }

    /// <summary>
    /// Seeds the shift, the tenant's BASIC component, one employee on it, and — unless
    /// <paramref name="weekdayMultiplier"/> is null — the tenant-default attendance-settings row carrying that
    /// weekday overtime multiplier. Returns the employee id.
    /// </summary>
    private async Task<Guid> SeedTenantAsync(Guid tenantId, decimal? weekdayMultiplier)
    {
        await using var db = Db(tenantId);

        SeedShift(db, tenantId);
        var employeeId = SeedEmployee(db, tenantId, "E1");

        db.SalaryComponents.Add(new SalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Basic Salary", Code = "BASIC",
            Type = SalaryComponentType.Earning, CalculationMethod = CalculationMethod.Fixed,
            IsTaxable = true, IsStatutory = false, IsActive = true, ProcessingOrder = 1,
        });
        AddSalary(db, tenantId, employeeId);

        // null ⇒ NO settings row at all (the state most tenants are in).
        if (weekdayMultiplier is { } multiplier)
            db.AttendanceSettings.Add(new AttendanceSettings
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
                LocationId = null,                            // the TENANT default row
                WeekdayOvertimeMultiplier = multiplier,
            });

        await db.SaveChangesAsync();
        return employeeId;
    }

    /// <summary>A second employee in an already-seeded tenant, on the same BASIC.</summary>
    private async Task<Guid> AddEmployeeAsync(Guid tenantId, string no)
    {
        await using var db = Db(tenantId);
        var employeeId = SeedEmployee(db, tenantId, no);
        AddSalary(db, tenantId, employeeId);
        await db.SaveChangesAsync();
        return employeeId;
    }

    /// <summary>
    /// Locks the period (once per tenant) and gives the employee fully-present attendance with 2h of APPROVED
    /// overtime at <paramref name="recordMultiplier"/> — plus a real clock-in, because ISSUE-090 omits an
    /// employee with no attendance data for the period entirely.
    ///
    /// <para>A <paramref name="recordMultiplier"/> of 0 is the point of this suite: the payroll pull keys the
    /// per-multiplier breakdown by <c>Multiplier.ToString("0.##")</c>, so a 0 produces the key "0", which
    /// <c>ParseMultiplier</c>'s <c>m &gt; 0m</c> guard rejects — leaving `defaultMultiplier` to price the block.
    /// That is the reachable shape of "overtime carrying no rate of its own".</para>
    /// </summary>
    private async Task SeedAttendanceAsync(Guid tenantId, Guid employeeId, decimal recordMultiplier)
    {
        await using var db = Db(tenantId);

        var monthStart = new DateOnly(Year, Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        if (!await db.AttendancePeriodLocks.AnyAsync(l => l.PeriodStart == monthStart))
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
            TotalPresentDays = ShiftWorkingDays,
            TotalAbsentDays = 0m,
            LopDays = 0m,
            TotalWorkMinutes = 0, TotalOvertimeMinutes = OtMinutes,
            GeneratedAt = DateTime.UtcNow,
        });

        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            ClockIn = new DateTime(Year, Month, 10, 9, 0, 0, DateTimeKind.Utc),
            ClockOut = new DateTime(Year, Month, 10, 17, 0, 0, DateTimeKind.Utc),
            TotalWorkMinutes = 480,
        });

        db.OvertimeRecords.Add(new OvertimeRecord
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            Date = new DateOnly(Year, Month, 10),
            OvertimeMinutes = OtMinutes, ApprovedMinutes = OtMinutes,
            Multiplier = recordMultiplier, Type = OvertimeType.PreApproved, Status = OvertimeStatus.Approved,
        });

        await db.SaveChangesAsync();
    }

    // ── running ────────────────────────────────────────────────────────

    /// <summary>Processes the period through the REAL PayrollRunProcessor and returns the slips by employee.</summary>
    private async Task<Dictionary<Guid, PayrollSlip>> RunAsync(Guid tenantId)
    {
        Guid runId;
        await using (var db = Db(tenantId))
        {
            runId = BaseEntity.NewUuidV7();
            db.PayrollRuns.Add(new PayrollRun
            {
                Id = runId, TenantId = tenantId, PayYear = Year, PayMonth = Month,
                Status = PayrollRunStatus.Queued, InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var provider = BuildProvider(tenantId))
        {
            var result = await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(runId);
            result.IsSuccess.Should().BeTrue(result.Error);
        }

        await using var read = Db(tenantId);
        return await read.PayrollSlips.AsNoTracking()
            .Where(s => s.PayrollRunId == runId)
            .ToDictionaryAsync(s => s.EmployeeId, s => s);
    }
}
