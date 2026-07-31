// ============================================================================
// D2 / BUG-293 — the LEAVE module owns the authoritative payroll LOP total, on REAL Postgres.
//
// Decision D2 (docs/vault/decisions/2026-07-31-comp-lop-entitlement.md): attendance stays the source of
// raw absence FACTS; the leave module applies the paid/unpaid POLICY and produces the LOP total payroll
// deducts. BUG-293: before this, PayrollRunProcessor read ONLY AttendanceMonthlySummary.LopDays, and an
// approved-but-UNPAID (IsLop) leave day is classified LEAVE by attendance (lop += 0) — so unpaid leave was
// PAID IN FULL. The fix routes payroll to ILopService.GetPayrollLopDaysAsync, which SUMS:
//   (a) attendance's raw figure (unapproved absence + US-ATT-008 lateness), and
//   (b) approved-and-unpaid leave days (IsLop && Status == Approved) — the DISJOINT complement of (a).
//
// Why real Postgres (this repo's standing rule): this is ledger/payroll ARITHMETIC on a money path, and
// prior money bugs (DF-65, BUG-124, BUG-291) were only caught on real PG — InMemory masks the EF query
// (IsLop/Status filter, half-day expansion, boundary clipping) and the whole compose→process→persist leg.
//
// Harness mirrors YtdCumulativeRunPostgresTests: one postgres:17-alpine container, migrations once,
// UseSnakeCaseNamingConvention(), the production interceptors, distinct GUIDs per test. The pipeline arms
// drive InitiatePayrollRunCommand → PayrollRunProcessor.ProcessAsync and read slip.LopDays back from a
// FRESH context. The seam arms call GetPayrollLopDaysAsync directly to pin the passthrough/expansion rules.
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
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class LopAuthorityPayrollPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenant = Guid.NewGuid();

    // The pay period under test (a locked, past month so LOP is applied — BR-3).
    private const int PayYear = 2026;
    private const int PayMonth = 6;
    private static readonly DateOnly PeriodStart = new(PayYear, PayMonth, 1);
    private static readonly DateOnly PeriodEnd = new(PayYear, PayMonth, 30);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db();
        await db.Database.MigrateAsync();
        // Tenant (FK parent) + the LOP + a regular (paid) leave type — one seed shared by every test.
        db.Tenants.Add(new Tenant { Id = _tenant, Subdomain = $"t{_tenant:N}"[..20], Name = "Co", DefaultCountryCode = "LK" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── harness ───────────────────────────────────────────────────────────────
    private sealed class TC : ITenantContext
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

    private sealed class CU : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
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

    private AppDbContext Db()
    {
        var tc = new TC { TenantId = _tenant };
        var cu = new CU { TenantId = _tenant };
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
                .Options, tc);
    }

    // A LopService on the real container — only AppDbContext + ITenantContext are exercised by
    // GetPayrollLopDaysAsync, so the leave-policy collaborators are substituted.
    private LopService NewLopService(AppDbContext db) => new(
        db, new TC { TenantId = _tenant }, new CU { TenantId = _tenant },
        Substitute.For<ILeaveTypeService>(), Substitute.For<IAttendanceProvider>(),
        Substitute.For<ILeaveNotificationService>(), NullLogger<LopService>.Instance,
        Substitute.For<ITenantLeaveYearResolver>());

    // Full composed payroll pipeline on real PG, WITH the D2 leave-LOP seam wired (ILopService registered).
    private IServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ITenantContext>(_ => new TC { TenantId = _tenant });
        services.AddScoped<ICurrentUser>(_ => new CU { TenantId = _tenant });
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
        services.AddScoped<IStatutoryRuleService, StatutoryRuleService>();
        // The D2 seam: the leave module owns the authoritative LOP total payroll reads.
        services.AddScoped<ILopService>(sp => new LopService(
            sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<ITenantContext>(),
            sp.GetRequiredService<ICurrentUser>(), Substitute.For<ILeaveTypeService>(),
            Substitute.For<IAttendanceProvider>(), Substitute.For<ILeaveNotificationService>(),
            NullLogger<LopService>.Instance, Substitute.For<ITenantLeaveYearResolver>()));
        services.AddScoped<IPayrollRunProcessor, PayrollRunProcessor>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(InitiatePayrollRunCommand).Assembly));
        return services.BuildServiceProvider();
    }

    // ── seeding ─────────────────────────────────────────────────────────────
    private async Task<Guid> SeedLeaveType(string name, LeaveTypeSystemCategory category)
    {
        await using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.LeaveTypes.Add(new LeaveType
        {
            Id = id, TenantId = _tenant, Name = name, Code = name[..3].ToUpperInvariant(),
            AnnualEntitlement = 0m, SystemCategory = category, IsActive = true, HalfDayAllowed = true,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<(Guid deptId, Guid titleId)> SeedDeptAndTitle()
    {
        await using var db = Db();
        var deptId = BaseEntity.NewUuidV7();
        var titleId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = deptId, TenantId = _tenant, Name = "Engineering", Code = "ENG" });
        db.JobTitles.Add(new JobTitle { Id = titleId, TenantId = _tenant, TitleName = "Engineer" });
        await db.SaveChangesAsync();
        return (deptId, titleId);
    }

    private async Task<Guid> SeedEmployeeWithSalary(string no, Guid deptId, Guid titleId, decimal monthlyBasic = 300_000m)
    {
        await using var db = Db();
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = _tenant, EmployeeNo = no, FirstName = no, LastName = "X",
            Email = $"{no}@t.com", DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = deptId, JobTitleId = titleId, LocationId = null,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        var componentId = BaseEntity.NewUuidV7();
        db.SalaryComponents.Add(new SalaryComponent
        {
            Id = componentId, TenantId = _tenant, Name = "Basic Salary", Code = "BASIC",
            Type = SalaryComponentType.Earning, CalculationMethod = CalculationMethod.Fixed,
            IsActive = true, ProcessingOrder = 1,
        });
        db.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EmployeeId = empId,
            SalaryStructureId = BaseEntity.NewUuidV7(), SalaryComponentId = componentId,
            AnnualAmount = monthlyBasic * 12m, MonthlyAmount = monthlyBasic,
            EffectiveFrom = new DateOnly(2020, 1, 1), EffectiveTo = null,
        });
        await db.SaveChangesAsync();
        return empId;
    }

    /// <summary>Seeds this employee's AttendanceMonthlySummary — attendance's OWN figure (unapproved absence +
    /// lateness). This is attendance's persisted OUTPUT, exactly the seam payroll consumes; the test never
    /// changes how attendance computes it (it excludes approved-leave days → those are NOT in lopDays here).</summary>
    private async Task SeedSummary(Guid empId, decimal lopDays, decimal absentDays)
    {
        await using var db = Db();
        db.AttendanceMonthlySummaries.Add(new AttendanceMonthlySummary
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EmployeeId = empId,
            YearMonth = $"{PayYear:D4}-{PayMonth:D2}", TotalPresentDays = 20m, TotalAbsentDays = absentDays,
            LopDays = lopDays, TotalOvertimeMinutes = 0, GeneratedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task LockPeriod()
    {
        await using var db = Db();
        if (await db.AttendancePeriodLocks.AnyAsync(l => l.PeriodStart == PeriodStart))
            return;
        db.AttendancePeriodLocks.Add(new AttendancePeriodLock
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant,
            PeriodStart = PeriodStart, PeriodEnd = PeriodEnd, IsLocked = true, LockedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedLeave(Guid empId, Guid leaveTypeId, DateOnly start, DateOnly end,
        bool isLop, LeaveRequestStatus status, bool isHalfDay = false)
    {
        await using var db = Db();
        decimal totalDays = isHalfDay && start == end ? 0.5m : end.DayNumber - start.DayNumber + 1;
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenant, EmployeeId = empId, LeaveTypeId = leaveTypeId,
            StartDate = start, EndDate = end, IsHalfDay = isHalfDay,
            HalfDaySession = isHalfDay ? "AM" : null, TotalDays = totalDays,
            Status = status, RequestedAt = DateTime.UtcNow,
            IsLop = isLop, LopSource = isLop ? LopSource.EmployeeRequest : null,
        });
        await db.SaveChangesAsync();
    }

    private async Task<decimal> RunAndReadLop(IServiceProvider provider, Guid empId)
    {
        await LockPeriod();
        var mediator = provider.GetRequiredService<IMediator>();
        var init = await mediator.Send(new InitiatePayrollRunCommand(PayMonth, PayYear, null));
        init.IsSuccess.Should().BeTrue(init.Error);
        (await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId))
            .IsSuccess.Should().BeTrue();
        await using var db = Db();
        return (await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.EmployeeId == empId && s.PayMonth == PayMonth))
            .LopDays;
    }

    // ═══════════════════════════ PIPELINE ARMS (slip.LopDays) ═══════════════════════════

    // BUG-293 regression: an APPROVED unpaid (IsLop) leave day is now DEDUCTED in payroll. Attendance excludes
    // it (lop = 0), so before D2 this was paid in full. THIS ARM FAILS AGAINST CURRENT MAIN (mutation (a)).
    [Fact]
    [Trait("TC", "TC-PAY-003-18")]
    public async Task ApprovedUnpaidLeave_IsDeductedInPayroll_OnPostgres_Bug293()
    {
        var lopType = await SeedLeaveType("Loss of Pay", LeaveTypeSystemCategory.LossOfPay);
        var (dept, title) = await SeedDeptAndTitle();
        var emp = await SeedEmployeeWithSalary("BUG293", dept, title);
        await SeedSummary(emp, lopDays: 0m, absentDays: 0m);              // attendance sees only LEAVE → lop 0
        await SeedLeave(emp, lopType, new DateOnly(PayYear, PayMonth, 10),
            new DateOnly(PayYear, PayMonth, 11), isLop: true, LeaveRequestStatus.Approved); // 2 unpaid days

        (await RunAndReadLop(Provider(), emp)).Should().Be(2m,
            "the two approved-but-unpaid leave days are the leave module's authoritative LOP; before D2 payroll " +
            "read only the attendance figure (0) and paid them in full");
    }

    // Over-correction guard: an APPROVED PAID leave (not IsLop) still deducts NOTHING.
    [Fact]
    [Trait("TC", "TC-PAY-003-18")]
    public async Task ApprovedPaidLeave_IsNotDeducted_OnPostgres()
    {
        var paidType = await SeedLeaveType("Annual", LeaveTypeSystemCategory.None);
        var (dept, title) = await SeedDeptAndTitle();
        var emp = await SeedEmployeeWithSalary("PAID", dept, title);
        await SeedSummary(emp, lopDays: 0m, absentDays: 0m);
        await SeedLeave(emp, paidType, new DateOnly(PayYear, PayMonth, 5),
            new DateOnly(PayYear, PayMonth, 7), isLop: false, LeaveRequestStatus.Approved); // 3 paid days

        (await RunAndReadLop(Provider(), emp)).Should().Be(0m,
            "paid leave is not IsLop, so the leave rail adds nothing and attendance excludes it → no deduction");
    }

    // Disjointness: ONE employee, ONE month, with unapproved absence + lateness (attendance) AND approved-unpaid
    // leave AND approved-paid leave AND an HR-assigned LOP on the SAME absent day. The total is the EXACT sum,
    // counted ONCE each. THIS ARM FAILS UNDER mutation (b) (relaxing the Status==Approved leave filter, which
    // would re-count the HR-assigned day attendance already deducts).
    [Fact]
    [Trait("TC", "TC-PAY-003-18")]
    public async Task Disjoint_AbsenceLatenessUnpaidLeavePaidLeave_SumExactlyOnce_OnPostgres()
    {
        var lopType = await SeedLeaveType("Loss of Pay", LeaveTypeSystemCategory.LossOfPay);
        var paidType = await SeedLeaveType("Annual", LeaveTypeSystemCategory.None);
        var (dept, title) = await SeedDeptAndTitle();
        var emp = await SeedEmployeeWithSalary("DISJOINT", dept, title);

        // Attendance's own figure: 1 unapproved-absence day + 0.5 lateness = 1.5 (AbsentDays=1 → late portion 0.5).
        await SeedSummary(emp, lopDays: 1.5m, absentDays: 1m);
        // Approved unpaid leave: +2. Approved paid leave: +0. HR-assigned LOP on the absent day: must NOT re-count.
        await SeedLeave(emp, lopType, new DateOnly(PayYear, PayMonth, 10),
            new DateOnly(PayYear, PayMonth, 11), isLop: true, LeaveRequestStatus.Approved);
        await SeedLeave(emp, paidType, new DateOnly(PayYear, PayMonth, 14),
            new DateOnly(PayYear, PayMonth, 15), isLop: false, LeaveRequestStatus.Approved);
        await SeedLeave(emp, lopType, new DateOnly(PayYear, PayMonth, 20),
            new DateOnly(PayYear, PayMonth, 20), isLop: true, LeaveRequestStatus.HrAssigned);

        (await RunAndReadLop(Provider(), emp)).Should().Be(3.5m,
            "1 absence + 0.5 lateness (attendance) + 2 approved-unpaid leave; paid leave and the HR-assigned LOP " +
            "day (already in attendance's absence figure) add nothing — the two rails are disjoint");
    }

    // Half-day: a single-day half-day approved-unpaid leave contributes 0.5, once.
    [Fact]
    [Trait("TC", "TC-PAY-003-18")]
    public async Task HalfDayApprovedUnpaidLeave_CountsHalf_OnPostgres()
    {
        var lopType = await SeedLeaveType("Loss of Pay", LeaveTypeSystemCategory.LossOfPay);
        var (dept, title) = await SeedDeptAndTitle();
        var emp = await SeedEmployeeWithSalary("HALF", dept, title);
        await SeedSummary(emp, lopDays: 0m, absentDays: 0m);            // worked the other half → attendance lop 0
        await SeedLeave(emp, lopType, new DateOnly(PayYear, PayMonth, 9),
            new DateOnly(PayYear, PayMonth, 9), isLop: true, LeaveRequestStatus.Approved, isHalfDay: true);

        (await RunAndReadLop(Provider(), emp)).Should().Be(0.5m,
            "a single-day half-day unpaid leave is 0.5 LOP, counted once");
    }

    // ═══════════════════════════ SEAM ARMS (GetPayrollLopDaysAsync) ═══════════════════════════

    // Passthrough: with NO leave rows, the authoritative figure is EXACTLY attendance's own (absence + lateness),
    // unchanged — proving the leave rail never touches attendance's owned facts (arms 3 + 4, "unchanged").
    [Fact]
    [Trait("TC", "TC-PAY-003-18")]
    public async Task Seam_AttendanceFigure_PassesThroughUnchanged_WhenNoLeave_OnPostgres()
    {
        var (dept, title) = await SeedDeptAndTitle();
        var emp = await SeedEmployeeWithSalary("PASS", dept, title);
        await using var db = Db();

        var result = await NewLopService(db).GetPayrollLopDaysAsync(
            new[] { emp }, PeriodStart, PeriodEnd,
            new Dictionary<Guid, decimal> { [emp] = 2.5m });   // 2 absence + 0.5 lateness, attendance-owned

        result[emp].Should().Be(2.5m, "no leave rows → the leave rail adds nothing; attendance's figure is authoritative");
    }

    // Boundary clipping: an approved-unpaid leave that STRADDLES the month contributes only its IN-PERIOD days.
    [Fact]
    [Trait("TC", "TC-PAY-003-18")]
    public async Task Seam_StraddlingLeave_ContributesOnlyInPeriodDays_OnPostgres()
    {
        var lopType = await SeedLeaveType("Loss of Pay", LeaveTypeSystemCategory.LossOfPay);
        var (dept, title) = await SeedDeptAndTitle();
        var emp = await SeedEmployeeWithSalary("STRADDLE", dept, title);
        // May 29 → Jun 2: only Jun 1 + Jun 2 fall in the June period → 2 days.
        await SeedLeave(emp, lopType, new DateOnly(PayYear, 5, 29), new DateOnly(PayYear, 6, 2),
            isLop: true, LeaveRequestStatus.Approved);
        await using var db = Db();

        var result = await NewLopService(db).GetPayrollLopDaysAsync(
            new[] { emp }, PeriodStart, PeriodEnd, new Dictionary<Guid, decimal> { [emp] = 0m });

        result[emp].Should().Be(2m, "only the two in-period days (Jun 1, Jun 2) of the straddling request count");
    }
}
