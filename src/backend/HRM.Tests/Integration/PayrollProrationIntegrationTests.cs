// ============================================================================
// Payroll join/separation pro-ration cluster — ISSUE-156 (joiner), ISSUE-157 (leaver),
// double-pro-ration guard. Exercises the full PayrollRunProcessor.ProcessAsync path through the real
// composed pipeline, asserting the slip's PaidDays / WorkingDays as CONCRETE day counts (money-critical).
//
// Core rule under test: a mid-month joiner/leaver is pro-rated by SHIFT working-days over the employed
// sub-range (like the run engine's own working-days), NOT a calendar-day fraction. All numbers below are
// for September 2025 (Sep 1 = Monday) with a Mon-Fri default shift:
//   - full-month Mon-Fri working-days .......... 22
//   - joiner DOJ Sep 18 → Sep 30 (Mon-Fri) .....  9   (old calendar-fraction formula gave 9.53)
//   - leaver terminated Sep 10 → Sep 1..10 ......  8   (old formula gave a FULL month — the bug)
//
// PROVIDER: InMemory through the real composed pipeline — same rationale as the sibling Payroll/Attendance
// integration tests (the verify gate runs `dotnet test` with no PostgreSQL / Docker). ShiftScheduleResolver
// uses only LINQ (no raw SQL), so it runs identically on InMemory and Npgsql.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Commands;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration;

public sealed class PayrollProrationIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();

    private const int Year = 2025;
    private const int Month = 9;   // September 2025 — Sep 1 is a Monday; 22 Mon-Fri working days.

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
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = tenantId });
        services.AddSingleton<IPayrollRunJobScheduler>(new CapturingScheduler());
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
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(InitiatePayrollRunCommand).Assembly));
        return services.BuildServiceProvider();
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    /// <summary>Seeds a tenant-default Mon-Fri shift so every unassigned employee counts Mon-Fri working days.</summary>
    private async Task SeedMonFriDefaultShift(Guid tenantId)
    {
        using var db = Db(tenantId);
        db.Shifts.Add(new Shift
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Standard Mon-Fri",
            Type = ShiftType.Single, WorkingDays = new List<int> { 1, 2, 3, 4, 5 },
            IsDefault = true, IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds a Mon-SAT default shift (ISO 1..6) — proves the pro-ration actually consults the shift set.</summary>
    private async Task SeedMonSatDefaultShift(Guid tenantId)
    {
        using var db = Db(tenantId);
        db.Shifts.Add(new Shift
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Standard Mon-Sat",
            Type = ShiftType.Single, WorkingDays = new List<int> { 1, 2, 3, 4, 5, 6 },
            IsDefault = true, IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds an active employee with a current BASIC salary component + a given date of joining.</summary>
    private async Task<Guid> SeedEmployee(Guid tenantId, string no, decimal monthlyBasic, DateTime dateOfJoining)
    {
        using var db = Db(tenantId);
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "X",
            Email = $"{no}@t.com", DateOfJoining = dateOfJoining,
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
            EffectiveFrom = new DateOnly(2019, 1, 1), EffectiveTo = null,
        });
        await db.SaveChangesAsync();
        return empId;
    }

    /// <summary>ISSUE-157: records a status_change → Terminated employment-history row effective on the given date.</summary>
    private async Task SeedTermination(Guid tenantId, Guid employeeId, DateOnly effective)
    {
        using var db = Db(tenantId);
        db.EmploymentHistories.Add(new EmploymentHistory
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            ChangeType = "status_change", PreviousValue = "Active", NewValue = "Terminated",
            EffectiveDate = effective.ToDateTime(TimeOnly.MinValue), ChangedBy = "hr@t.com",
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds a real attendance punch so the employee is surfaced WITH an attendance row (ISSUE-090).</summary>
    private async Task SeedAttendanceLog(Guid tenantId, Guid employeeId, DateTime clockInUtc)
    {
        using var db = Db(tenantId);
        db.AttendanceLogs.Add(new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            ClockIn = clockInUtc, Source = "WEB",
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Locks the attendance period (initiate precondition) and seeds a fully-present materialized summary
    /// (LopDays = 0) for every employee, so LOP never interferes with the pro-ration day-count assertions.
    /// </summary>
    private async Task LockAttendance(Guid tenantId)
    {
        using var db = Db(tenantId);
        var start = new DateOnly(Year, Month, 1);
        db.AttendancePeriodLocks.Add(new AttendancePeriodLock
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
            PeriodStart = start, PeriodEnd = start.AddMonths(1).AddDays(-1),
            IsLocked = true, LockedAt = DateTime.UtcNow,
        });

        var yearMonth = $"{Year:D4}-{Month:D2}";
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

    private async Task<PayrollSlip> RunAndGetSlip(Guid tenantId, Guid employeeId)
    {
        var provider = Provider(tenantId);
        var mediator = provider.GetRequiredService<IMediator>();
        var init = await mediator.Send(new InitiatePayrollRunCommand(Month, Year, null));
        init.IsSuccess.Should().BeTrue(init.Error);
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        using var db = Db(tenantId);
        return await db.PayrollSlips.AsNoTracking()
            .SingleAsync(s => s.PayrollRunId == init.Value.RunId && s.EmployeeId == employeeId);
    }

    // ── ISSUE-156: mid-month joiner is pro-rated by SHIFT working-days, not the calendar fraction ──────────

    [Fact]
    [Trait("Issue", "ISSUE-156")]
    public async Task Joiner_MidMonth_PaidDays_AreShiftWorkingDays_NotCalendarFraction()
    {
        await SeedMonFriDefaultShift(_tenantA);
        // DOJ Sep 18, 2025 (Thursday). Shift working-days Sep 18..30 = 9. The OLD calendar-fraction formula
        // (workingDays * employedCalendarDays/totalCalendarDays = 22 * 13/30) produced 9.53 — this asserts 9.
        var empId = await SeedEmployee(_tenantA, "J1", 30_000m, new DateTime(2025, 9, 18));
        await LockAttendance(_tenantA);

        var slip = await RunAndGetSlip(_tenantA, empId);

        slip.PaidDays.Should().Be(9m);
    }

    // ── ISSUE-156 (money): no-attendance-row joiner — factor must be single-basis (shift/shift), not shift/calendar ──

    [Fact]
    [Trait("Issue", "ISSUE-156")]
    public async Task Joiner_MidMonth_NoAttendanceRow_Gross_UsesShiftDenominator_NotCalendar()
    {
        await SeedMonFriDefaultShift(_tenantA);
        // DOJ Sep 18 → 9 shift working-days over 22 full-month shift days. NO attendance log → the denominator
        // falls back to the SHIFT full-month count (22), so gross = 22000 * 9/22 = 9000. The mixed-basis bug
        // used the CALENDAR denominator (30) → 22000 * 9/30 = 6600 (under-payment).
        var empId = await SeedEmployee(_tenantA, "J3", 22_000m, new DateTime(2025, 9, 18));
        await LockAttendance(_tenantA);

        var slip = await RunAndGetSlip(_tenantA, empId);

        slip.PaidDays.Should().Be(9m);
        slip.WorkingDays.Should().Be(22m, "the fallback denominator must be the SHIFT full-month count, not calendar days (30)");
        slip.GrossEarnings.Should().Be(9_000m, "the pro-ration factor must be single-basis 9/22, not the mixed 9/30 = 6600");
    }

    // ── ISSUE-156 (shift-shape guard): the SAME joiner on a Mon-SAT shift gets a DIFFERENT count, proving the
    //    resolved shift set is actually consulted (a hard-coded Mon-Fri impl would still return 9 and FAIL here). ──
    [Fact]
    [Trait("Issue", "ISSUE-156")]
    public async Task Joiner_MidMonth_SatInclusiveShift_PaidDays_ReflectTheShiftSet()
    {
        await SeedMonSatDefaultShift(_tenantA);
        // DOJ Sep 18 (Thu). Mon-Sat working-days Sep 18..30 = 11 (the 9 Mon-Fri days + Sat 20 + Sat 27;
        // Sundays 21 & 28 excluded). Distinct from the Mon-Fri answer of 9 → pins that the shift set drives it.
        var empId = await SeedEmployee(_tenantA, "J1", 30_000m, new DateTime(2025, 9, 18));
        await LockAttendance(_tenantA);

        var slip = await RunAndGetSlip(_tenantA, empId);

        slip.PaidDays.Should().Be(11m);
    }

    // ── ISSUE-157: mid-month leaver with NO attendance row is bounded to worked days, not a full month ──────

    [Fact]
    [Trait("Issue", "ISSUE-157")]
    public async Task Leaver_MidMonth_NoAttendanceRow_PaidDays_BoundedToTermination_NotFullMonth()
    {
        await SeedMonFriDefaultShift(_tenantA);
        // Joined long ago (full-month eligible), but terminated effective Sep 10, 2025 (Wednesday). The OLD
        // engine returned null (full month) for a DOJ <= month start → the leaver was paid the whole month.
        // Shift working-days Sep 1..10 = 8.
        var empId = await SeedEmployee(_tenantA, "L1", 30_000m, new DateTime(2019, 1, 1));
        await SeedTermination(_tenantA, empId, new DateOnly(2025, 9, 10));
        await LockAttendance(_tenantA);

        var slip = await RunAndGetSlip(_tenantA, empId);

        slip.PaidDays.Should().Be(8m);
        slip.PaidDays.Should().BeLessThan(22m, "the leaver must not be paid a full month of working days");
    }

    // ── Double-pro-ration guard: a joiner WITH an attendance row is pro-rated exactly ONCE ─────────────────

    [Fact]
    [Trait("Issue", "ISSUE-156")]
    public async Task Joiner_WithAttendanceRow_IsProRatedExactlyOnce()
    {
        await SeedMonFriDefaultShift(_tenantA);
        // DOJ Sep 18 → 9 shift working-days. BASIC 22000 so the single-pro-rated gross is a clean 22000*9/22.
        var empId = await SeedEmployee(_tenantA, "J2", 22_000m, new DateTime(2025, 9, 18));
        await SeedAttendanceLog(_tenantA, empId, new DateTime(2025, 9, 22, 9, 0, 0, DateTimeKind.Utc));
        await LockAttendance(_tenantA);

        var slip = await RunAndGetSlip(_tenantA, empId);

        // The attendance pull does NOT start-bound joiners → WorkingDays is the full-month shift count (22).
        slip.WorkingDays.Should().Be(22m);
        // PaidDays is the single DOJ-bounded shift count (9). Double pro-ration would yield ~3.68 (9 * 9/22).
        slip.PaidDays.Should().Be(9m);
        // At the money level: gross = 22000 * 9/22 = 9000 exactly (single pro-ration), not 22000*(9/22)^2 ≈ 3681.
        slip.GrossEarnings.Should().Be(9_000m);
    }
}
