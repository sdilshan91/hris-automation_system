// ============================================================================
// US-PAY-003: Payroll-run engine — integration tests.
//
// Exercises the full handler -> service/processor -> DbContext path through a real DI container (MediatR
// pipeline + ITenantContext-driven global query filters), covering:
//   - AC-1/FR-2: initiate creates a Queued run and enqueues the processing job (captured via a fake
//     IPayrollRunJobScheduler), returning the runId.
//   - AC-2/AC-3/BR-6: the processor transitions Queued -> Processing -> ReviewPending, persists slips, and
//     stamps summary totals (FR-8).
//   - AC-4/BR-1: a duplicate run for a finalized/in-progress period returns 409.
//   - AC-6: an employee without a salary structure is skipped with a run-log warning; the run continues.
//   - AC-7: tenant isolation — Tenant A's run excludes Tenant B's employees.
//   - FR-9: a re-used Idempotency-Key returns 409.
//
// PROVIDER: InMemory through the real composed pipeline — same rationale as the other Payroll/Attendance
// integration tests (the verify gate runs `dotnet test` with no PostgreSQL / Docker). For Hangfire, the
// processor's compute is invoked directly (no live Hangfire server), per the story brief.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Commands;
using HRM.Application.Features.Payroll.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration;

public sealed class PayrollRunIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

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

    /// <summary>Captures the enqueue so the test can assert FR-2 without a live Hangfire server.</summary>
    private sealed class CapturingScheduler : IPayrollRunJobScheduler
    {
        public List<(Guid TenantId, Guid RunId)> Enqueued { get; } = new();
        public string Enqueue(Guid tenantId, string tenantSubdomain, Guid runId)
        {
            Enqueued.Add((tenantId, runId));
            return Guid.NewGuid().ToString();
        }
    }

    private CapturingScheduler _scheduler = new();

    private IServiceProvider Provider(Guid tenantId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = tenantId });
        services.AddSingleton<IPayrollRunJobScheduler>(_scheduler);
        services.AddSingleton<IPayrollNotificationService, LogOnlyPayrollNotificationService>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        // Attendance dependencies for the processor's attendance pull (best-effort; returns no rows here).
        services.AddScoped<IOvertimeService, OvertimeService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IAttendanceSummaryService, AttendanceSummaryService>();
        services.AddScoped<IAttendancePayrollService, AttendancePayrollService>();
        services.AddScoped<IReportExportStorage, InMemoryExportStorage>();
        services.AddScoped<IStatutoryDeductionResolver, StatutoryDeductionResolver>();
        services.AddScoped<IPayrollAdjustmentResolver, PayrollAdjustmentResolver>();
        services.AddScoped<IPayrollAuditLogger, PayrollAuditLogger>();
        services.AddScoped<IPayrollRunService, PayrollRunService>();
        services.AddScoped<IPayrollRunProcessor, PayrollRunProcessor>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(InitiatePayrollRunCommand).Assembly));
        return services.BuildServiceProvider();
    }

    private sealed class InMemoryExportStorage : IReportExportStorage
    {
        public Task<string> SaveAsync(Guid tenantId, Guid reportId, string fileName,
            string contentType, byte[] content, CancellationToken cancellationToken = default)
            => Task.FromResult($"mem://{tenantId}/{reportId}/{fileName}");
    }

    private IMediator Pipeline(Guid tenantId) => Provider(tenantId).GetRequiredService<IMediator>();

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    /// <summary>Seeds an active employee with a current BASIC salary component (a "salary structure").
    /// ISSUE-165: optionally links a department + job title so the generation-time snapshot is non-null.</summary>
    private async Task<Guid> SeedEmployeeWithSalary(Guid tenantId, string no, decimal monthlyBasic,
        bool withSalary = true, Guid? departmentId = null, Guid? jobTitleId = null)
    {
        using var db = Db(tenantId);
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "X",
            Email = $"{no}@t.com", DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = departmentId ?? Guid.Empty, JobTitleId = jobTitleId ?? Guid.Empty,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });

        if (withSalary)
        {
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
        }

        await db.SaveChangesAsync();
        return empId;
    }

    /// <summary>
    /// US-PAY-010 AC-4/FR-6 fixture: locks the attendance period so the payroll initiate gate passes. This
    /// reflects US-PAY-003's own precondition ("attendance data is finalized") — without it, initiate now
    /// returns 409 attendance_not_finalized. A fixture correction, NOT a weakening of any assertion.
    /// </summary>
    private async Task LockAttendance(Guid tenantId, int year, int month)
    {
        using var db = Db(tenantId);
        var start = new DateOnly(year, month, 1);
        db.AttendancePeriodLocks.Add(new AttendancePeriodLock
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
            PeriodStart = start, PeriodEnd = start.AddMonths(1).AddDays(-1),
            IsLocked = true, LockedAt = DateTime.UtcNow,
        });

        // A finalized period has materialized attendance summaries. Without them the on-demand summary
        // fabricates full-month absence (no clock-ins) → full LOP → net 0. Seed a FULLY-PRESENT summary
        // (LopDays = 0, no overtime) for every employee so these net=gross assertions hold. Tests that want
        // LOP/overtime seed their own summaries on top.
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

    // ── AC-1 / FR-2: initiate creates Queued + enqueues ─────────────────────

    [Fact]
    public async Task Initiate_CreatesQueuedRun_AndEnqueuesJob()
    {
        await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockAttendance(_tenantA, 2026, 5);
        var mediator = Pipeline(_tenantA);

        var result = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(PayrollRunStatus.Queued));
        result.Value.RunId.Should().NotBeEmpty();
        _scheduler.Enqueued.Should().ContainSingle(e => e.RunId == result.Value.RunId && e.TenantId == _tenantA);
    }

    // ── ISSUE-165: point-in-time department/designation snapshot stamped at generation ──

    /// <summary>
    /// The processor must stamp the resolved department + designation NAMES onto each slip at generation, so a
    /// later rename / department move never rewrites the historical slip. Seed an employee in dept "Engineering"
    /// / title "Engineer", process the run, and assert the persisted slip carries those exact snapshot strings.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-165")]
    public async Task Process_StampsDepartmentAndDesignationSnapshot_OnSlip()
    {
        var deptId = BaseEntity.NewUuidV7();
        var titleId = BaseEntity.NewUuidV7();
        using (var seed = Db(_tenantA))
        {
            seed.Departments.Add(new Department { Id = deptId, TenantId = _tenantA, Name = "Engineering", Code = "ENG-165", IsActive = true });
            seed.JobTitles.Add(new JobTitle { Id = titleId, TenantId = _tenantA, TitleName = "Engineer", IsActive = true });
            await seed.SaveChangesAsync();
        }
        await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m, departmentId: deptId, jobTitleId: titleId);
        await LockAttendance(_tenantA, 2026, 5);

        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        var init = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        using var db = Db(_tenantA);
        var slip = await db.PayrollSlips.AsNoTracking().SingleAsync(s => s.PayrollRunId == init.Value.RunId);
        slip.DepartmentSnapshot.Should().Be("Engineering");
        slip.DesignationSnapshot.Should().Be("Engineer");
    }

    // ── AC-2 / AC-3 / BR-6 / FR-8: processor transitions + totals ───────────

    [Fact]
    public async Task Process_TransitionsToReviewPending_PersistsSlips_AndStampsTotals()
    {
        await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await SeedEmployeeWithSalary(_tenantA, "A2", 30_000m);
        await LockAttendance(_tenantA, 2026, 5);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        var init = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        var runId = init.Value!.RunId;

        var processor = provider.GetRequiredService<IPayrollRunProcessor>();
        var processed = await processor.ProcessAsync(runId);
        processed.IsSuccess.Should().BeTrue();

        var run = await mediator.Send(new GetPayrollRunQuery(runId));
        run.Value!.Status.Should().Be(nameof(PayrollRunStatus.ReviewPending));
        run.Value.ProcessedEmployees.Should().Be(2);
        run.Value.SkippedEmployees.Should().Be(0);
        run.Value.TotalGross.Should().Be(80_000m);   // 50k + 30k
        run.Value.TotalNet.Should().Be(80_000m);

        using var db = Db(_tenantA);
        var slipCount = await db.PayrollSlips.CountAsync(s => s.PayrollRunId == runId);
        slipCount.Should().Be(2);
        var detailCount = await db.PayrollSlipDetails.CountAsync();
        detailCount.Should().BeGreaterThanOrEqualTo(2);
    }

    // ── US-PAY-012 / BUG-080: run completion emits a system-actor audit ──────

    [Fact]
    public async Task Process_ToReviewPending_EmitsPayrollRunCompletedAudit_AsSystemActor()
    {
        await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockAttendance(_tenantA, 2026, 5);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        var init = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        var runId = init.Value!.RunId;
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(runId);

        using var db = Db(_tenantA);
        var entry = await db.AuditLogs.AsNoTracking().SingleAsync(a =>
            a.Action == PayrollAuditAction.PayrollRunCompleted && a.ResourceId == runId.ToString());
        entry.ResourceType.Should().Be(PayrollAuditAction.ResourceType.PayrollRun);
        entry.TenantId.Should().Be(_tenantA);
        // BR-7: a Hangfire-job write is attributed to the well-known system actor, never the empty Guid.
        entry.UserId.Should().Be(PayrollAuditAction.SystemActorUserId);
        entry.After.Should().NotBeNullOrWhiteSpace();
    }

    // ── AC-6: employee without a salary structure is skipped, run continues ──

    [Fact]
    public async Task Process_EmployeeWithoutSalary_IsSkipped_RunContinues()
    {
        await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await SeedEmployeeWithSalary(_tenantA, "A2", 0m, withSalary: false);
        await LockAttendance(_tenantA, 2026, 5);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        var init = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        var summary = await mediator.Send(new GetPayrollRunSummaryQuery(init.Value.RunId));
        summary.Value!.Run.ProcessedEmployees.Should().Be(1);
        summary.Value.Run.SkippedEmployees.Should().Be(1);
        summary.Value.RunLog.Should().Contain("SKIPPED");
    }

    // ── AC-4 / BR-1: duplicate run for an in-progress period → 409 ──────────

    [Fact]
    public async Task Initiate_DuplicatePeriod_Returns409()
    {
        await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockAttendance(_tenantA, 2026, 5);
        var mediator = Pipeline(_tenantA);

        var first = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        first.IsSuccess.Should().BeTrue();

        var second = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        second.IsSuccess.Should().BeFalse();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("run_in_progress");
    }

    // ── AC-4: an already-finalized period → 409 period_already_finalized ────

    [Fact]
    public async Task Initiate_FinalizedPeriod_Returns409_AlreadyFinalized()
    {
        await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockAttendance(_tenantA, 2026, 5);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        var first = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        // Finalize the run directly (BR-7) to set up the AC-4 precondition.
        using (var db = Db(_tenantA))
        {
            var run = await db.PayrollRuns.FirstAsync(r => r.Id == first.Value!.RunId);
            run.Status = PayrollRunStatus.Finalized;
            run.FinalizedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var second = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        second.IsSuccess.Should().BeFalse();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("period_already_finalized");
    }

    // ── FR-9: re-used Idempotency-Key → 409 ─────────────────────────────────

    [Fact]
    public async Task Initiate_DuplicateIdempotencyKey_Returns409()
    {
        await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockAttendance(_tenantA, 2026, 5);
        await LockAttendance(_tenantA, 2026, 6);
        var mediator = Pipeline(_tenantA);

        var first = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, "key-123"));
        first.IsSuccess.Should().BeTrue();

        // Different period, same key → still rejected (a key must identify one request).
        var second = await mediator.Send(new InitiatePayrollRunCommand(6, 2026, "key-123"));
        second.IsSuccess.Should().BeFalse();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("duplicate_idempotency_key");
    }

    // ── ISSUE-153 / FR-9: replaying the same key for the SAME period is idempotent — returns the existing run ──
    [Fact]
    public async Task Initiate_SameIdempotencyKey_SamePeriod_ReturnsExistingRun_NoDuplicate()
    {
        await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockAttendance(_tenantA, 2026, 5);
        var mediator = Pipeline(_tenantA);

        var first = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, "key-abc"));
        first.IsSuccess.Should().BeTrue();

        // Replay: same key + SAME period → idempotent SUCCESS returning the SAME run (was a 409 before ISSUE-153).
        var replay = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, "key-abc"));
        replay.IsSuccess.Should().BeTrue(replay.Error);
        replay.Value!.RunId.Should().Be(first.Value!.RunId);

        // And no duplicate run was created for the key.
        using var db = Db(_tenantA);
        (await db.PayrollRuns.IgnoreQueryFilters().CountAsync(r => r.IdempotencyKey == "key-abc"))
            .Should().Be(1);
    }

    // ── AC-7: tenant isolation — Tenant A's run excludes Tenant B's employees ─

    [Fact]
    public async Task Process_TenantA_ExcludesTenantBEmployees()
    {
        await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await SeedEmployeeWithSalary(_tenantB, "B1", 99_000m);
        await LockAttendance(_tenantA, 2026, 5);

        var providerA = Provider(_tenantA);
        var mediatorA = providerA.GetRequiredService<IMediator>();
        var init = await mediatorA.Send(new InitiatePayrollRunCommand(5, 2026, null));
        await providerA.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        var run = await mediatorA.Send(new GetPayrollRunQuery(init.Value.RunId));
        run.Value!.ProcessedEmployees.Should().Be(1);     // only Tenant A's employee.
        run.Value.TotalGross.Should().Be(50_000m);        // Tenant B's 99k is excluded.

        // Tenant B cannot see Tenant A's run at all (global query filter).
        var mediatorB = Pipeline(_tenantB);
        var bView = await mediatorB.Send(new GetPayrollRunQuery(init.Value.RunId));
        bView.IsSuccess.Should().BeFalse();
        bView.StatusCode.Should().Be(404);
    }

    // ── FR-7: re-running a ReviewPending run replaces prior slips ───────────

    [Fact]
    public async Task Process_ReRun_ReplacesPriorSlips()
    {
        await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockAttendance(_tenantA, 2026, 5);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        var processor = provider.GetRequiredService<IPayrollRunProcessor>();

        var init = await mediator.Send(new InitiatePayrollRunCommand(5, 2026, null));
        await processor.ProcessAsync(init.Value!.RunId);
        await processor.ProcessAsync(init.Value.RunId);   // re-run (FR-7).

        using var db = Db(_tenantA);
        var slips = await db.PayrollSlips.CountAsync(s => s.PayrollRunId == init.Value.RunId);
        slips.Should().Be(1);                              // not duplicated.
    }
}
