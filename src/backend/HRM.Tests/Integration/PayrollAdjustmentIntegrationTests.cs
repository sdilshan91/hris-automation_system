// ============================================================================
// US-PAY-007: Payroll adjustments — integration tests.
//
// Exercises the full handler -> service/resolver -> processor -> DbContext path through a real DI container
// (MediatR pipeline + ITenantContext-driven global query filters), covering:
//   - AC-1/FR-3/FR-4: create an adjustment for a period -> run payroll -> the adjustment appears as a slip
//     detail line AND its status flips to Applied with the run id.
//   - AC-2/BR-3: a Deduction adjustment is subtracted from net.
//   - BR-2: a taxable bonus increases gross; a deduction larger than gross surfaces the negative-net warning.
//   - AC-4/BR-7: an adjustment targeting a Finalized period is deferred to the next period.
//   - FR-5/BR-6: a recurring adjustment auto-creates the correct number of future Pending occurrences.
//   - FR-7/BR-5: a Correction references the original slip and shows as an Arrears line.
//   - FR-7: re-running a run re-applies the same adjustments (no double application / no dropped lines).
//   - AC-5: tenant isolation — one tenant's adjustments are invisible to another.
//   - REGRESSION: with NO adjustments, the run behaves exactly as US-PAY-003 (totals unchanged).
//
// PROVIDER: InMemory through the real composed pipeline — same rationale as the other Payroll/Attendance
// integration tests (the verify gate runs `dotnet test` with no PostgreSQL / Docker).
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

public sealed class PayrollAdjustmentIntegrationTests
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

    private sealed class CapturingScheduler : IPayrollRunJobScheduler
    {
        public string Enqueue(Guid tenantId, string tenantSubdomain, Guid runId) => Guid.NewGuid().ToString();
    }

    /// <summary>In-memory IFileStorage so document upload/download is exercised without a real filesystem.</summary>
    private sealed class InMemoryFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public async Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, cancellationToken);
            _store[$"{tenantId}/{relativePath}"] = ms.ToArray();
            return relativePath;
        }
        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.TryGetValue($"{tenantId}/{relativePath}", out var b) ? (Stream?)new MemoryStream(b) : null);
        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null) => $"mem://{tenantId}/{relativePath}";
        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
        {
            _store.Remove($"{tenantId}/{relativePath}");
            return Task.CompletedTask;
        }
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
        services.AddSingleton<IFileStorage>(new InMemoryFileStorage());
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IOvertimeService, OvertimeService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IAttendanceSummaryService, AttendanceSummaryService>();
        services.AddScoped<IAttendancePayrollService, AttendancePayrollService>();
        services.AddScoped<IReportExportStorage, InMemoryExportStorage>();
        services.AddScoped<IPayrollAuditLogger, PayrollAuditLogger>();
        services.AddScoped<IStatutoryDeductionResolver, StatutoryDeductionResolver>();
        services.AddScoped<IPayrollAdjustmentResolver, PayrollAdjustmentResolver>();
        services.AddScoped<IPayrollAdjustmentService, PayrollAdjustmentService>();
        services.AddScoped<IPayrollSlipCleaner, PayrollSlipCleaner>();
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

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private async Task<Guid> SeedEmployeeWithSalary(Guid tenantId, string no, decimal monthlyBasic, bool withSalary = true)
    {
        using var db = Db(tenantId);
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "X",
            Email = $"{no}@t.com", DateOfJoining = new DateTime(2020, 1, 1),
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
    /// US-PAY-010 AC-4/FR-6 fixture: locks all of <paramref name="year"/> so the payroll initiate gate passes
    /// for any month a test runs. This reflects US-PAY-003's own "attendance finalized" precondition — without
    /// the lock, initiate now returns 409 attendance_not_finalized. A fixture correction, NOT a weakening.
    /// </summary>
    private async Task LockYear(Guid tenantId, int year)
    {
        using var db = Db(tenantId);
        db.AttendancePeriodLocks.Add(new AttendancePeriodLock
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
            PeriodStart = new DateOnly(year, 1, 1), PeriodEnd = new DateOnly(year, 12, 31),
            IsLocked = true, LockedAt = DateTime.UtcNow,
        });

        // A finalized period has materialized attendance summaries. Without them the on-demand summary
        // fabricates full-month absence → full LOP → net 0. Seed a FULLY-PRESENT summary (LopDays = 0, no
        // overtime) for every employee for every month of the year so the adjustment net/gross assertions hold.
        var employeeIds = await db.Employees.Select(e => e.Id).ToListAsync();
        foreach (var empId in employeeIds)
        {
            for (var month = 1; month <= 12; month++)
            {
                db.AttendanceMonthlySummaries.Add(new AttendanceMonthlySummary
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = empId,
                    YearMonth = $"{year:D4}-{month:D2}", TotalPresentDays = 22m, TotalAbsentDays = 0m,
                    LopDays = 0m, TotalOvertimeMinutes = 0, GeneratedAt = DateTime.UtcNow,
                });
            }
        }
        await db.SaveChangesAsync();
    }

    private static CreatePayrollAdjustmentCommand Bonus(Guid empId, decimal amount, int m, int y, bool taxable = false)
        => new(empId, nameof(AdjustmentType.Bonus), amount, "Performance bonus", m, y, taxable, false, null, null, null);

    private static CreatePayrollAdjustmentCommand Deduction(Guid empId, decimal amount, int m, int y)
        => new(empId, nameof(AdjustmentType.Deduction), amount, "One-time deduction", m, y, false, false, null, null, null);

    // ── AC-1 / FR-3 / FR-4: create -> run -> appears as a slip line + Applied ───

    [Fact]
    public async Task Bonus_AppearsAsSlipLine_AndIsMarkedApplied()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockYear(_tenantA, 2026);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        var create = await mediator.Send(Bonus(emp, 10_000m, 6, 2026));
        create.IsSuccess.Should().BeTrue();
        create.Value!.Adjustment.Status.Should().Be(nameof(AdjustmentStatus.Pending));

        var init = await mediator.Send(new InitiatePayrollRunCommand(6, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        var run = await mediator.Send(new GetPayrollRunQuery(init.Value.RunId));
        run.Value!.TotalGross.Should().Be(60_000m);   // 50k basic + 10k bonus.
        run.Value.TotalNet.Should().Be(60_000m);

        using var db = Db(_tenantA);
        var slip = await db.PayrollSlips.FirstAsync(s => s.PayrollRunId == init.Value.RunId && s.EmployeeId == emp);
        var details = await db.PayrollSlipDetails.Where(d => d.PayrollSlipId == slip.Id).ToListAsync();
        details.Should().Contain(d => d.ComponentName.Contains("Bonus"));

        var adj = await db.PayrollAdjustments.FirstAsync(a => a.EmployeeId == emp);
        adj.Status.Should().Be(AdjustmentStatus.Applied);
        adj.AppliedInPayrollRunId.Should().Be(init.Value.RunId);
    }

    // ── BR-2: a taxable bonus is in gross BEFORE statutory tax runs (flows to tax) ──

    [Fact]
    public async Task TaxableBonus_IncreasesGross_AndFlowsToTax()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 600_000m); // high monthly basic so tax > 0.
        await LockYear(_tenantA, 2026);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        // Configure an income-tax rule in effect for the period (FY 2026-2027, slabs as the US-PAY-006 tests).
        using (var db = Db(_tenantA))
        {
            var ruleId = BaseEntity.NewUuidV7();
            db.StatutoryRules.Add(new StatutoryRule
            {
                Id = ruleId, TenantId = _tenantA, RuleType = StatutoryRuleType.IncomeTax, RuleName = "PAYE",
                CountryCode = "LK", FiscalYear = "2026-2027",
                EffectiveFrom = new DateOnly(2026, 4, 1), EffectiveTo = new DateOnly(2027, 3, 31), IsActive = true,
            });
            db.TaxSlabs.AddRange(
                new TaxSlab { Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, StatutoryRuleId = ruleId, SlabFrom = 0m, SlabTo = 250_000m, RatePercentage = 0m, OrderIndex = 0 },
                new TaxSlab { Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, StatutoryRuleId = ruleId, SlabFrom = 250_000m, SlabTo = 500_000m, RatePercentage = 5m, OrderIndex = 1 },
                new TaxSlab { Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, StatutoryRuleId = ruleId, SlabFrom = 500_000m, SlabTo = 1_000_000m, RatePercentage = 20m, OrderIndex = 2 },
                new TaxSlab { Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, StatutoryRuleId = ruleId, SlabFrom = 1_000_000m, SlabTo = null, RatePercentage = 30m, OrderIndex = 3 });
            await db.SaveChangesAsync();
        }

        // Baseline run with NO bonus → capture the tax (a statutory deduction line).
        var baseInit = await mediator.Send(new InitiatePayrollRunCommand(6, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(baseInit.Value!.RunId);
        var baseRun = await mediator.Send(new GetPayrollRunQuery(baseInit.Value.RunId));
        var baseGross = baseRun.Value!.TotalGross;
        var baseDeductions = baseRun.Value.TotalDeductions;

        // Finalize the baseline so the next run is a clean different period (July) carrying the bonus.
        using (var db = Db(_tenantA))
        {
            var r = await db.PayrollRuns.FirstAsync(x => x.Id == baseInit.Value.RunId);
            r.Status = PayrollRunStatus.Finalized; r.FinalizedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        // Taxable 200k bonus for July.
        await mediator.Send(Bonus(emp, 200_000m, 7, 2026, taxable: true));
        var bonusInit = await mediator.Send(new InitiatePayrollRunCommand(7, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(bonusInit.Value!.RunId);
        var bonusRun = await mediator.Send(new GetPayrollRunQuery(bonusInit.Value.RunId));

        bonusRun.Value!.TotalGross.Should().Be(baseGross + 200_000m);     // bonus is in gross.
        bonusRun.Value.TotalDeductions.Should().BeGreaterThan(baseDeductions); // and drives MORE tax (BR-2).
    }

    // ── AC-2 / BR-3: a deduction is subtracted from net ─────────────────────────

    [Fact]
    public async Task Deduction_IsSubtractedFromNet()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockYear(_tenantA, 2026);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Send(Deduction(emp, 5_000m, 6, 2026));
        var init = await mediator.Send(new InitiatePayrollRunCommand(6, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        var run = await mediator.Send(new GetPayrollRunQuery(init.Value.RunId));
        run.Value!.TotalGross.Should().Be(50_000m);
        run.Value.TotalDeductions.Should().Be(5_000m);
        run.Value.TotalNet.Should().Be(45_000m);   // 50k - 5k.
    }

    // ── BR-3: a deduction that would drive net negative warns at create time ─────

    [Fact]
    public async Task Deduction_ExceedingNet_RaisesNegativeNetWarning_OnSecondCreate()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockYear(_tenantA, 2026);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        // First run produces a 50k-net slip for the period.
        await mediator.Send(Deduction(emp, 1m, 6, 2026)); // trivial pending so the period exists; warning false (no slip yet).
        var init = await mediator.Send(new InitiatePayrollRunCommand(6, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        // Now a new big deduction against the same (now-computed) period → negative-net warning.
        var create = await mediator.Send(Deduction(emp, 999_000m, 6, 2026));
        create.IsSuccess.Should().BeTrue();           // BR-3 warns, does NOT hard-block.
        create.Value!.NegativeNetWarning.Should().BeTrue();
    }

    // ── BUG-074: the warning is now reachable for a normal FUTURE period (no run yet) ──
    // Load-bearing: this exercises the no-persisted-slip path. Before the fix WouldDriveNetNegativeAsync
    // returned false whenever no slip existed, so this warning was DEAD for every not-yet-run adjustment.

    [Fact]
    public async Task Deduction_ExceedingProjectedNet_OnFuturePeriod_RaisesWarning_WithNoRunYet()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m); // structure net ~50k/month.
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        // A far-future period that has NEVER been run — there is no persisted slip to estimate against, so the
        // projection is built from the employee's CURRENT salary structure (BUG-074).
        var create = await mediator.Send(Deduction(emp, 999_000m, 11, 2027));

        create.IsSuccess.Should().BeTrue();
        create.Value!.NegativeNetWarning.Should().BeTrue();

        // Confirm no run/slip exists — the projection, not a persisted slip, drove the warning.
        using var db = Db(_tenantA);
        (await db.PayrollSlips.AnyAsync(s => s.EmployeeId == emp)).Should().BeFalse();
    }

    [Fact]
    public async Task Deduction_WithinProjectedNet_OnFuturePeriod_DoesNotWarn()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        // A modest deduction that leaves projected net comfortably positive → no warning.
        var create = await mediator.Send(Deduction(emp, 5_000m, 11, 2027));

        create.IsSuccess.Should().BeTrue();
        create.Value!.NegativeNetWarning.Should().BeFalse();
    }

    // ── BUG-062: deductions exceeding gross are WARNED, never REJECTED (net can be legitimately negative) ──

    [Fact]
    public async Task Deduction_ExceedingGross_IsNotRejected_ButIsWarned()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m); // gross 50k.
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        // 55k deduction > 50k gross (e.g. a loan/advance over-recovery). BUG-062 decision: WARN, do not block.
        var create = await mediator.Send(Deduction(emp, 55_000m, 11, 2027));

        create.IsSuccess.Should().BeTrue();               // NOT rejected.
        create.Value!.Adjustment.Status.Should().Be(nameof(AdjustmentStatus.Pending)); // and persisted.
        create.Value.NegativeNetWarning.Should().BeTrue();  // but the advisory fired.
    }

    // ── AC-4 / BR-7: targeting a Finalized period defers to the next period ──────

    [Fact]
    public async Task AdjustmentForFinalizedPeriod_DefersToNextPeriod()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockYear(_tenantA, 2026);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        // Finalize June 2026.
        var init = await mediator.Send(new InitiatePayrollRunCommand(6, 2026, null));
        using (var db = Db(_tenantA))
        {
            var run = await db.PayrollRuns.FirstAsync(r => r.Id == init.Value!.RunId);
            run.Status = PayrollRunStatus.Finalized;
            run.FinalizedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var create = await mediator.Send(Bonus(emp, 10_000m, 6, 2026));
        create.IsSuccess.Should().BeTrue();
        create.Value!.DeferredToPayMonth.Should().Be(7);   // deferred to July 2026 (AC-4).
        create.Value.DeferredToPayYear.Should().Be(2026);
        create.Value.Adjustment.ApplicablePayMonth.Should().Be(7);
    }

    // ── FR-5 / BR-6: recurring creates the correct number of future pendings ─────

    [Fact]
    public async Task Recurring_CreatesCorrectNumberOfFuturePendings()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        // Jan 2026 .. Jun 2026 inclusive = 6 periods total (1 originating + 5 generated occurrences).
        var cmd = new CreatePayrollAdjustmentCommand(
            emp, nameof(AdjustmentType.Deduction), 2_000m, "Loan repayment",
            1, 2026, false, true, 6, 2026, null);
        var create = await mediator.Send(cmd);

        create.IsSuccess.Should().BeTrue();
        create.Value!.GeneratedOccurrences.Should().Be(5);

        using var db = Db(_tenantA);
        var count = await db.PayrollAdjustments.CountAsync(a => a.EmployeeId == emp);
        count.Should().Be(6);
        var series = await db.PayrollAdjustments.Where(a => a.EmployeeId == emp).Select(a => a.RecurringSeriesId).Distinct().ToListAsync();
        series.Should().ContainSingle().Which.Should().NotBeNull();   // one series id across all occurrences (BR-6).
    }

    // ── FR-7 / BR-5: a Correction references the original slip + Arrears line ────

    [Fact]
    public async Task Correction_ReferencesOriginalSlip_AndShowsAsArrears()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockYear(_tenantA, 2026);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        // June run produces the original slip.
        var june = await mediator.Send(new InitiatePayrollRunCommand(6, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(june.Value!.RunId);
        Guid originalSlipId;
        using (var db = Db(_tenantA))
            originalSlipId = (await db.PayrollSlips.FirstAsync(s => s.PayrollRunId == june.Value.RunId)).Id;

        // Correction for July referencing the June slip.
        var correction = new CreatePayrollAdjustmentCommand(
            emp, nameof(AdjustmentType.Correction), 3_000m, "Underpaid HRA",
            7, 2026, false, false, null, null, originalSlipId);
        var create = await mediator.Send(correction);
        create.IsSuccess.Should().BeTrue();
        create.Value!.Adjustment.ReferencePayrollSlipId.Should().Be(originalSlipId);

        // Run July → arrears line appears, referencing the original slip.
        var july = await mediator.Send(new InitiatePayrollRunCommand(7, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(july.Value!.RunId);

        using var db2 = Db(_tenantA);
        var julySlip = await db2.PayrollSlips.FirstAsync(s => s.PayrollRunId == july.Value.RunId);
        var details = await db2.PayrollSlipDetails.Where(d => d.PayrollSlipId == julySlip.Id).ToListAsync();
        details.Should().Contain(d => d.ComponentName.Contains("Arrears") && d.ComponentName.Contains(originalSlipId.ToString()));
        julySlip.GrossEarnings.Should().Be(53_000m);   // 50k + 3k arrears.
    }

    // ── FR-7: re-running re-applies the same adjustments (no dropped lines) ──────

    [Fact]
    public async Task ReRun_ReAppliesAdjustments_NoDoubleApplicationNoDroppedLines()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockYear(_tenantA, 2026);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        var processor = provider.GetRequiredService<IPayrollRunProcessor>();

        await mediator.Send(Bonus(emp, 10_000m, 6, 2026));
        var init = await mediator.Send(new InitiatePayrollRunCommand(6, 2026, null));
        await processor.ProcessAsync(init.Value!.RunId);
        await processor.ProcessAsync(init.Value.RunId);   // re-run (FR-7).

        var run = await mediator.Send(new GetPayrollRunQuery(init.Value.RunId));
        run.Value!.TotalGross.Should().Be(60_000m);       // bonus still applied once after the re-run.

        using var db = Db(_tenantA);
        var adj = await db.PayrollAdjustments.FirstAsync(a => a.EmployeeId == emp);
        adj.Status.Should().Be(AdjustmentStatus.Applied);
        adj.AppliedInPayrollRunId.Should().Be(init.Value.RunId);
        var slips = await db.PayrollSlips.CountAsync(s => s.PayrollRunId == init.Value.RunId);
        slips.Should().Be(1);                             // slips not duplicated.
    }

    // ── REGRESSION: no adjustments → run behaves exactly as US-PAY-003 ──────────

    [Fact]
    public async Task NoAdjustments_RunTotalsUnchanged()
    {
        await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockYear(_tenantA, 2026);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        var init = await mediator.Send(new InitiatePayrollRunCommand(6, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);

        var run = await mediator.Send(new GetPayrollRunQuery(init.Value.RunId));
        run.Value!.TotalGross.Should().Be(50_000m);
        run.Value.TotalNet.Should().Be(50_000m);
        run.Value.ProcessedEmployees.Should().Be(1);
    }

    // ── AC-5: tenant isolation — Tenant B cannot see Tenant A's adjustments ──────

    [Fact]
    public async Task TenantIsolation_AdjustmentsAreInvisibleAcrossTenants()
    {
        var empA = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await SeedEmployeeWithSalary(_tenantB, "B1", 50_000m);

        var providerA = Provider(_tenantA);
        var createA = await providerA.GetRequiredService<IMediator>().Send(Bonus(empA, 10_000m, 6, 2026));
        createA.IsSuccess.Should().BeTrue();

        // Tenant B lists adjustments → none of Tenant A's are visible.
        var mediatorB = Provider(_tenantB).GetRequiredService<IMediator>();
        var listB = await mediatorB.Send(new ListPayrollAdjustmentsQuery(null, null, null, null, null, 1, 25));
        listB.IsSuccess.Should().BeTrue();
        listB.Value!.Items.Should().BeEmpty();

        // Tenant B cannot fetch Tenant A's adjustment by id (global filter → 404).
        var getB = await mediatorB.Send(new GetPayrollAdjustmentQuery(createA.Value!.Adjustment.Id));
        getB.IsSuccess.Should().BeFalse();
        getB.StatusCode.Should().Be(404);
    }

    // ── BR-1: cannot create for an employee with no active salary assignment ─────

    [Fact]
    public async Task Create_ForEmployeeWithoutSalary_Fails()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 0m, withSalary: false);
        var mediator = Provider(_tenantA).GetRequiredService<IMediator>();

        var create = await mediator.Send(Bonus(emp, 10_000m, 6, 2026));
        create.IsSuccess.Should().BeFalse();
        create.StatusCode.Should().Be(422);
        create.ErrorCode.Should().Be("no_active_salary");
    }

    // ── FR-6: cancel a Pending adjustment; Applied cannot be cancelled ───────────

    [Fact]
    public async Task Cancel_Pending_Succeeds_Applied_Fails()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await LockYear(_tenantA, 2026);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        var create = await mediator.Send(Bonus(emp, 10_000m, 6, 2026));
        var cancel = await mediator.Send(new CancelPayrollAdjustmentCommand(create.Value!.Adjustment.Id));
        cancel.IsSuccess.Should().BeTrue();

        // A cancelled adjustment is excluded from the run.
        var init = await mediator.Send(new InitiatePayrollRunCommand(6, 2026, null));
        await provider.GetRequiredService<IPayrollRunProcessor>().ProcessAsync(init.Value!.RunId);
        var run = await mediator.Send(new GetPayrollRunQuery(init.Value.RunId));
        run.Value!.TotalGross.Should().Be(50_000m);   // bonus excluded.

        // Re-cancel → 409.
        var again = await mediator.Send(new CancelPayrollAdjustmentCommand(create.Value.Adjustment.Id));
        again.IsSuccess.Should().BeFalse();
        again.StatusCode.Should().Be(409);
    }

    // ── AC-3 / NFR-5: supporting document upload + download round-trips ──────────

    [Fact]
    public async Task SupportingDocument_UploadThenDownload_RoundTrips()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        var create = await mediator.Send(
            new CreatePayrollAdjustmentCommand(emp, nameof(AdjustmentType.Reimbursement), 1_500m, "Travel",
                6, 2026, false, false, null, null, null));
        var adjId = create.Value!.Adjustment.Id;

        var bytes = "%PDF-1.4 fake"u8.ToArray();
        var upload = await mediator.Send(new UploadAdjustmentDocumentCommand(
            adjId, new MemoryStream(bytes), "receipt.pdf", "application/pdf", bytes.Length));
        upload.IsSuccess.Should().BeTrue();
        upload.Value.Should().Contain($"payroll/adjustments/{adjId}/");

        var download = await mediator.Send(new DownloadAdjustmentDocumentQuery(adjId));
        download.IsSuccess.Should().BeTrue();
        download.Value!.Content.Should().Equal(bytes);
        download.Value.ContentType.Should().Be("application/pdf");
    }

    // ── NFR-5: an oversized / unsupported document is rejected ───────────────────

    [Fact]
    public async Task SupportingDocument_UnsupportedType_IsRejected()
    {
        var emp = await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        var mediator = Provider(_tenantA).GetRequiredService<IMediator>();

        var create = await mediator.Send(Bonus(emp, 1_000m, 6, 2026));
        var bytes = "malware"u8.ToArray();
        var upload = await mediator.Send(new UploadAdjustmentDocumentCommand(
            create.Value!.Adjustment.Id, new MemoryStream(bytes), "evil.exe", "application/octet-stream", bytes.Length));
        upload.IsSuccess.Should().BeFalse();
        upload.ErrorCode.Should().Be("unsupported_file_type");
    }

    // ── FR-2: bulk CSV upload creates valid rows, reports bad rows ───────────────

    [Fact]
    public async Task BulkUpload_CreatesValidRows_ReportsBadRows()
    {
        await SeedEmployeeWithSalary(_tenantA, "A1", 50_000m);
        await SeedEmployeeWithSalary(_tenantA, "A2", 30_000m);
        var mediator = Provider(_tenantA).GetRequiredService<IMediator>();

        var csv =
            "employee_no,adjustment_type,amount,description,is_taxable\n" +
            "A1,Bonus,5000,Festival bonus,true\n" +
            "A2,Deduction,2000,Advance recovery,false\n" +
            "A9,Bonus,1000,Unknown employee,false\n" +     // employee_not_found
            "A1,Foo,1000,Bad type,false\n";                // invalid_adjustment_type
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);

        var result = await mediator.Send(new BulkCreatePayrollAdjustmentsCommand(6, 2026, new MemoryStream(bytes)));
        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalRows.Should().Be(4);
        result.Value.SucceededCount.Should().Be(2);
        result.Value.FailedCount.Should().Be(2);
        result.Value.Results.Should().Contain(r => r.EmployeeNo == "A9" && r.ErrorCode == "employee_not_found");
        result.Value.Results.Should().Contain(r => r.EmployeeNo == "A1" && r.ErrorCode == "invalid_adjustment_type");
    }
}
