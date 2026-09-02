// ============================================================================
// GAP-022 / G1 — the PLUMBING of the FTE-scaled overtime base (US-ATT-011 AC-5 / US-CHR-013).
//
// ⚠ WHY THIS SUITE EXISTS. `PayrollOvertimeCalculator.Compute` has taken `fte` + `fteScaledBase` since
// CAL-6, and `OvertimeFteBaseTests` proves the MATH — that suite's own header concedes it "proves the
// MATH, not the plumbing". The plumbing was the defect: `PayrollRunProcessor.ComputeOvertime` called
// `Compute` with FOUR arguments, so `fte` was always 1.0 and `fteScaledBase` always false. A tenant that
// switched `AttendanceSettings.FteScaledOvertimeBase` ON (a persisted, API-settable flag) got the
// full-time hourly base for its part-timers on EVERY run — silently UNDER-paying their overtime.
//
// So every arm here goes through the REAL PayrollRunProcessor.ProcessAsync and asserts money off the
// PERSISTED slip. A calculator-level unit test cannot close this gap; only the run can.
//
// SEMANTICS (pinned by the domain doc, not by intuition): scaling divides the hourly base by
// `working_days * 8 * fte`, i.e. it HALVES the effective hours the monthly basic buys for a 0.5-FTE
// employee, which DOUBLES their OT hourly rate. "That basic buys half the hours, so each hour is worth
// double" — PayrollOvertimeCalculator.cs and AttendanceSettings.FteScaledOvertimeBase both say so.
//
// WHY POSTGRES: this asserts money out of the real run over the real migration DDL — including the
// `fte_scaled_overtime_base` DEFAULT false and `employees.fte` DEFAULT 1.00, which are exactly the
// defaults the no-regression arms depend on. InMemory would not exercise either.
//
// GOLDEN MONTH: September 2025 — Sep 1 is a Monday, so a Mon–Fri shift has exactly 22 working days.
//   BASIC 22,000/month, 2h of approved OT at 2x:
//     flag OFF (any FTE) : hourly = 22000/(22*8)       = 125.00 → OT = 2 * 125.00 * 2 =   500.00
//     flag ON, 1.0 FTE   : hourly = 22000/(22*8*1.0)   = 125.00 → OT =                    500.00
//     flag ON, 0.5 FTE   : hourly = 22000/(22*8*0.5)   = 250.00 → OT =                  1,000.00
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

public sealed class PayrollFteOvertimeBaseTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private const int Year = 2025;
    private const int Month = 9;               // Sep 2025 — Sep 1 is a Monday.
    private const int ShiftWorkingDays = 22;   // Mon–Fri days in Sep 2025.
    private const decimal Basic = 22000m;
    private const int OtMinutes = 120;         // 2 hours…
    private const decimal OtMultiplier = 2m;   // …at 2x, so the amount is a clean multiple of the hourly rate.

    private const decimal FullTimeHourly = 125.00m;    // 22000 / (22 * 8)
    private const decimal HalfFteHourly = 250.00m;     // 22000 / (22 * 8 * 0.5)
    private const decimal UnscaledOtAmount = 500.00m;  // 2h * 125.00 * 2x
    private const decimal ScaledOtAmount = 1000.00m;   // 2h * 250.00 * 2x

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(Guid.NewGuid());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ══════════════════════════════════════════════════════════════════════
    //  ARM 1 — the defect: flag ON + a part-timer, through the REAL run
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TC-ATT-152 (AC-5, plumbing): ONE tenant with the flag ON and ONE run containing a 1.0-FTE and a
    /// 0.5-FTE employee on the SAME monthly basic and the SAME approved overtime. The part-timer's OT hourly
    /// base must be EXACTLY double the full-timer's, because their basic buys half the hours.
    ///
    /// <para>This is the arm that FAILS before the fix: the processor called the calculator without the FTE
    /// arguments, so both employees were priced at 125.00/h and the part-timer was paid 500.00 instead of
    /// 1,000.00 — a 50% under-payment of overtime, on every run, silently.</para>
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-152")]
    public async Task FlagOn_HalfFteEmployee_OvertimeHourlyBaseIsDouble_ThroughTheRealRun()
    {
        var tenantId = Guid.NewGuid();
        var (fullTimer, partTimer) = await SeedTenantAsync(tenantId, fteScaledOvertimeBase: true);

        var slips = await RunAsync(tenantId);

        slips[partTimer].OvertimeAmount.Should().Be(
            ScaledOtAmount, "a 0.5-FTE employee's monthly basic buys half the hours, so each OT hour is worth double");
        slips[fullTimer].OvertimeAmount.Should().Be(
            UnscaledOtAmount, "a 1.0-FTE employee scales by 1 — the flag must not disturb full-timers");
        slips[partTimer].OvertimeAmount.Should().Be(
            slips[fullTimer].OvertimeAmount * 2m, "exactly double, not merely greater");

        // The hourly base itself, off the persisted slip line's calculation basis ("2h @ 250/h").
        (await OvertimeBasisAsync(tenantId, slips[partTimer].Id)).Should().Contain(
            HalfFteHourly.ToString("0.##", CultureInfo.InvariantCulture),
            "the slip must EXPLAIN the scaled rate, not just carry the amount");
        (await OvertimeBasisAsync(tenantId, slips[fullTimer].Id)).Should().Contain(
            FullTimeHourly.ToString("0.##", CultureInfo.InvariantCulture));
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ARM 2 — CONTROL: flag OFF (the default) ⇒ FTE changes NOTHING
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TC-ATT-152 (control): with the flag OFF — the code default and every existing tenant — a 0.5-FTE and a
    /// 1.0-FTE employee on the same basic get the SAME overtime. This is the no-regression contract: threading
    /// FTE through the run must not move a single default-policy tenant's money.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-152")]
    public async Task FlagOff_HalfFteEmployee_OvertimeIsUnchanged_NoRegressionForDefaultTenants()
    {
        var tenantId = Guid.NewGuid();
        var (fullTimer, partTimer) = await SeedTenantAsync(tenantId, fteScaledOvertimeBase: false);

        var slips = await RunAsync(tenantId);

        slips[partTimer].OvertimeAmount.Should().Be(
            UnscaledOtAmount, "the flag is OFF, so FTE must not touch the base");
        slips[fullTimer].OvertimeAmount.Should().Be(UnscaledOtAmount);
        slips[partTimer].NetSalary.Should().Be(slips[fullTimer].NetSalary, "identical basic + identical OT");
    }

    /// <summary>
    /// TC-ATT-152 (control): a tenant with NO attendance-settings row at all — the run must not create one and
    /// must read the code default (OFF). Guards the "policy row absent" branch of the resolver, which is the
    /// state most tenants are actually in.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-152")]
    public async Task NoSettingsRow_OvertimeIsUnscaled_AndTheRunDoesNotCreatePolicy()
    {
        var tenantId = Guid.NewGuid();
        var (fullTimer, partTimer) = await SeedTenantAsync(tenantId, fteScaledOvertimeBase: null);

        var slips = await RunAsync(tenantId);

        slips[partTimer].OvertimeAmount.Should().Be(UnscaledOtAmount, "no policy row ⇒ code default (OFF)");
        slips[fullTimer].OvertimeAmount.Should().Be(UnscaledOtAmount);

        await using var db = Db(tenantId);
        (await db.AttendanceSettings.AsNoTracking().CountAsync()).Should().Be(
            0, "a payroll run must never write attendance POLICY as a side effect");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ARM 3 — the policy is resolved per LOCATION, not tenant-wide
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TC-ATT-152 (US-ATT-011 AC-3): the flag is read off the employee's EFFECTIVE policy — their Location's
    /// override wins wholesale over the tenant default. A branch that opted in scales; an employee outside that
    /// branch, in the same run, does not. Distinguishes "resolved per employee" from "read one arbitrary row".
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-152")]
    public async Task LocationOverrideOn_WhileTenantDefaultOff_ScalesOnlyThatLocationsEmployee()
    {
        var tenantId = Guid.NewGuid();
        var (fullTimer, partTimer) = await SeedTenantAsync(tenantId, fteScaledOvertimeBase: false);

        // A THIRD employee: 0.5 FTE, at a Location whose override turns the flag ON.
        Guid branchPartTimer;
        Guid locationId;
        await using (var db = Db(tenantId))
        {
            var location = new Location
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Branch", TimeZone = "UTC", IsActive = true,
            };
            db.Locations.Add(location);
            locationId = location.Id;

            branchPartTimer = SeedEmployee(db, tenantId, "E3", fte: 0.50m, locationId: location.Id);
            AddSalary(db, tenantId, branchPartTimer);

            db.AttendanceSettings.Add(new AttendanceSettings
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
                LocationId = location.Id, FteScaledOvertimeBase = true,
            });
            await db.SaveChangesAsync();
        }

        await SeedAttendanceAsync(tenantId, branchPartTimer);

        var slips = await RunAsync(tenantId);

        slips[branchPartTimer].OvertimeAmount.Should().Be(
            ScaledOtAmount, "the Location override (ON) wins wholesale over the tenant default (OFF)");
        slips[partTimer].OvertimeAmount.Should().Be(
            UnscaledOtAmount, "an employee with no Location falls back to the tenant default (OFF)");
        slips[fullTimer].OvertimeAmount.Should().Be(UnscaledOtAmount);
        locationId.Should().NotBeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ARM 4 — a corrupt FTE must not become a divide-by-zero on a money path
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TC-ATT-152: an employee row carrying FTE = 0 (not reachable through the validators, but reachable
    /// through a bad import/backfill) must fall back to the UNSCALED base rather than throwing or paying a
    /// negative. The guard lives in <c>PayrollOvertimeCalculator</c>; this arm pins that the processor DELEGATES
    /// to it instead of reimplementing the scaling — a run that divided by the raw FTE would fail the whole run.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-ATT-152")]
    public async Task FlagOn_CorruptZeroFte_FallsBackToTheUnscaledBase_AndTheRunStillSucceeds()
    {
        var tenantId = Guid.NewGuid();
        var (_, partTimer) = await SeedTenantAsync(tenantId, fteScaledOvertimeBase: true);

        await using (var db = Db(tenantId))
        {
            var emp = await db.Employees.SingleAsync(e => e.Id == partTimer);
            emp.Fte = 0m;
            await db.SaveChangesAsync();
        }

        var slips = await RunAsync(tenantId);

        slips[partTimer].OvertimeAmount.Should().Be(
            UnscaledOtAmount, "a corrupt FTE reads as unscaled, never as a divide-by-zero or negative pay");
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

    /// <summary>The payroll run's DI graph — mirrors PayrollWorkingDaysDenominatorTests.BuildProvider.</summary>
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

    private static Guid SeedEmployee(AppDbContext db, Guid tenantId, string no, decimal fte, Guid? locationId = null)
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
            Fte = fte,
        });
        return id;
    }

    /// <summary>
    /// The tenant's single BASIC component (unique on (tenant, code)) plus an assignment for each employee.
    /// </summary>
    private static void SeedSalary(AppDbContext db, Guid tenantId, params Guid[] employeeIds)
    {
        db.SalaryComponents.Add(new SalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Basic Salary", Code = "BASIC",
            Type = SalaryComponentType.Earning, CalculationMethod = CalculationMethod.Fixed,
            IsTaxable = true, IsStatutory = false, IsActive = true, ProcessingOrder = 1,
        });

        foreach (var employeeId in employeeIds)
            AddSalary(db, tenantId, employeeId);
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
    /// Seeds the shift, a 1.0-FTE and a 0.5-FTE employee on the same basic, their attendance, and — unless
    /// <paramref name="fteScaledOvertimeBase"/> is null — the tenant-default attendance-settings row.
    /// Returns (fullTimer, partTimer).
    /// </summary>
    private async Task<(Guid FullTimer, Guid PartTimer)> SeedTenantAsync(Guid tenantId, bool? fteScaledOvertimeBase)
    {
        Guid fullTimer, partTimer;
        await using (var db = Db(tenantId))
        {
            SeedShift(db, tenantId);
            fullTimer = SeedEmployee(db, tenantId, "E1", fte: 1.00m);
            partTimer = SeedEmployee(db, tenantId, "E2", fte: 0.50m);
            SeedSalary(db, tenantId, fullTimer, partTimer);

            // null ⇒ NO settings row at all (the state most tenants are in).
            if (fteScaledOvertimeBase is { } flag)
                db.AttendanceSettings.Add(new AttendanceSettings
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
                    LocationId = null,               // the TENANT default row
                    FteScaledOvertimeBase = flag,
                });

            await db.SaveChangesAsync();
        }

        await SeedAttendanceAsync(tenantId, fullTimer);
        await SeedAttendanceAsync(tenantId, partTimer);
        return (fullTimer, partTimer);
    }

    /// <summary>
    /// Locks the period (once per tenant) and gives the employee identical, fully-present attendance with 2h of
    /// APPROVED overtime at 2x — plus a real clock-in, because ISSUE-090 omits an employee with no attendance
    /// data for the period entirely.
    /// </summary>
    private async Task SeedAttendanceAsync(Guid tenantId, Guid employeeId)
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
            Multiplier = OtMultiplier, Type = OvertimeType.PreApproved, Status = OvertimeStatus.Approved,
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

    /// <summary>The overtime line's calculation basis on a slip ("2h @ 250/h").</summary>
    private async Task<string> OvertimeBasisAsync(Guid tenantId, Guid slipId)
    {
        await using var db = Db(tenantId);
        var basis = await db.PayrollSlipDetails.AsNoTracking()
            .Where(d => d.PayrollSlipId == slipId && d.ComponentName == "Overtime")
            .Select(d => d.CalculationBasis)
            .FirstOrDefaultAsync();
        return basis ?? string.Empty;
    }
}
