// ============================================================================
// DF-61 — real-Postgres (Testcontainers) coverage for the payroll REPROCESS path.
//
// Two behaviours that InMemory cannot prove and that only bite on real Npgsql:
//
//   (A) DF-61-pg — the reconcile marker (PayrollRunReconcileJob).
//       The DF-61 sweep re-enqueues a ReviewPending run whose reprocess was
//       REQUESTED (ReprocessRequestedAt stamped by RerunAsync) but whose enqueue
//       was dropped, and RESETS the marker so it re-fires at most once per
//       StaleThreshold. The InMemory arms (PayrollRunReconcileJobTests) prove the
//       branch logic; these arms prove the marker WRITE actually COMMITS on
//       Postgres (asserted from a FRESH DbContext, not just the tracked entity) —
//       the "InMemory-masks-Postgres" bug class this repo keeps hitting.
//
//   (B) DF-61-conc — the per-runId interlock on PayrollRunProcessor.ProcessAsync.
//       A Postgres SESSION advisory lock (pg_try_advisory_lock, keyed on the runId)
//       stops two concurrent ProcessAsync on the SAME run from both replace-cleaning
//       + re-inserting slips (duplicate ReviewPending slips — never a double-PAY).
//       Advisory locks are a Postgres-only mechanism, so this REQUIRES real PG.
//       Proven deterministically (no thread race) by holding the SAME lock from a
//       separate raw Npgsql session, then observing ProcessAsync no-op. Also proves
//       the lock is per-runId scoped (a DIFFERENT run is NOT serialized) and that a
//       ProcessAsync whose reprocess was already satisfied (ReviewPending + NULL
//       marker) no-ops — the retry-after-completion edge.
//
// Harness mirrors FinalSettlementPostgresTests / MoneyPathsPostgresTests: a single
// postgres:17-alpine container per class, migrations applied once in InitializeAsync,
// UseSnakeCaseNamingConvention() (omitting it throws PendingModelChangesWarning),
// EnableRetryOnFailure, distinct GUIDs/subdomains per test so the shared DB does not
// cross-contaminate (assertions are scoped to each test's own runId/tenant).
// ============================================================================

using System.Data.Common;
using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class PayrollReprocessReconcilePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    // Fixed clock: Aged is comfortably past the 10-min StaleThreshold; Fresh is inside the fast-path window.
    private static readonly DateTime NowUtc = DateTime.SpecifyKind(new DateTime(2026, 6, 15, 12, 0, 0), DateTimeKind.Utc);
    private static readonly TimeProvider FixedClock = new FixedTimeProvider(new DateTimeOffset(NowUtc));
    private static readonly DateTime Aged = NowUtc.AddMinutes(-20);
    private static readonly DateTime Fresh = NowUtc.AddMinutes(-2);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var tc = new FixedTenantContext { TenantId = Guid.NewGuid() };
        await using var db = NewDb(tc);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── harness ───────────────────────────────────────────────────────────

    private sealed class FixedTenantContext : ITenantContext
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private AppDbContext NewDb(ITenantContext tc, IInterceptor? extra = null)
    {
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");

        var interceptors = extra is null
            ? new IInterceptor[] { new TenantInterceptor(tc), new AuditInterceptor(cu) }
            : [new TenantInterceptor(tc), new AuditInterceptor(cu), extra];

        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(interceptors)
                .Options,
            tc);
    }

    /// <summary>
    /// Test-only seam that reproduces the DF-61-conc-approval-race window DETERMINISTICALLY, with no change to
    /// production code.
    ///
    /// <para>The race is: <c>ProcessAsync</c> reads the run (ReviewPending — the pre-lock guard passes), takes
    /// the advisory lock, then RE-READS. HR approves in between. Only the post-lock re-read can notice, so
    /// seeding the run as Approved up front proves nothing about that site — the pre-lock guard catches it
    /// first and the post-lock guard is never reached.</para>
    ///
    /// <para>This interceptor fires immediately after the FIRST <c>payroll_runs</c> SELECT and flips the row to
    /// the target status from a SEPARATE committed connection. By the time <c>ReloadAsync</c> issues the second
    /// SELECT, the run really has been approved underneath the worker — exactly the production sequence.</para>
    /// </summary>
    private sealed class ApproveBetweenReadsInterceptor(string connectionString, Guid runId, PayrollRunStatus flipTo)
        : DbCommandInterceptor
    {
        private int _fired;

        public bool DidFlip => Volatile.Read(ref _fired) > 0;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            // Only the run-row read, and only once — the pre-lock read. NOTE the table is payroll_run
            // (SINGULAR); matching the pluralised name silently never fires, which is what the DidFlip
            // vacuity guard in the arm exists to catch. Interlock SQL (pg_try_advisory_lock)
            // and every other table's queries are ignored.
            if (command.CommandText.Contains("payroll_run", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await using var side = new NpgsqlConnection(connectionString);
                await side.OpenAsync(cancellationToken);
                await using var cmd = side.CreateCommand();
                cmd.CommandText = "UPDATE payroll_run SET status = @s WHERE id = @id";
                var s = cmd.CreateParameter(); s.ParameterName = "s"; s.Value = flipTo.ToString();
                var id = cmd.CreateParameter(); id.ParameterName = "id"; id.Value = runId;
                cmd.Parameters.Add(s);
                cmd.Parameters.Add(id);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>Seeds a Tenant (FK parent) + a PayrollRun with an explicit TenantId. Returns the runId.</summary>
    private async Task<Guid> SeedRunAsync(
        Guid tenantId, string subdomain, TenantStatus tenantStatus, PayrollRunStatus runStatus,
        DateTime initiatedAt, DateTime? reprocessRequestedAt = null,
        DateTime? completedAt = null, decimal totalGross = 0m, int payMonth = 5)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        await using var db = NewDb(tc);

        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId))
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, Subdomain = subdomain, Name = subdomain, Status = tenantStatus,
                FiscalYearStartMonth = 1, DefaultCountryCode = "LK",
            });

        var runId = Guid.NewGuid();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId,
            TenantId = tenantId,
            PayYear = 2026,
            PayMonth = payMonth,
            Status = runStatus,
            InitiatedAt = initiatedAt,
            ReprocessRequestedAt = reprocessRequestedAt,
            CompletedAt = completedAt,
            TotalGross = totalGross,
        });
        await db.SaveChangesAsync();
        return runId;
    }

    private ServiceProvider BuildReconcileProvider(IPayrollRunJobScheduler scheduler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // Rls:Enabled defaults false
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention());
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser>(_ =>
        {
            var cu = Substitute.For<ICurrentUser>();
            cu.IsAuthenticated.Returns(false);
            return cu;
        });
        services.AddScoped<ITenantJobRunner, TenantJobRunner>();
        services.AddSingleton(scheduler);
        return services.BuildServiceProvider();
    }

    private async Task RunSweepAsync(ServiceProvider provider) =>
        await new PayrollRunReconcileJob(provider.GetRequiredService<IServiceScopeFactory>(), FixedClock).RunAsync();

    /// <summary>A PayrollRunProcessor on real PG with substituted collaborators — enough to drive a ZERO-employee
    /// run to completion (unlocked period → no attendance pull; empty adjustments/statutory). The AppDbContext it
    /// returns is the one whose connection the advisory lock is taken on.</summary>
    private (AppDbContext db, PayrollRunProcessor processor) NewProcessor(
        Guid tenantId, IInterceptor? extraInterceptor = null)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        var db = NewDb(tc, extraInterceptor);

        var adjustments = Substitute.For<IPayrollAdjustmentResolver>();
        adjustments.ResolveForPeriodAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, EmployeeAdjustments>());

        // The statutory resolver IS reached for a payable employee even with no StatutoryRules seeded — the
        // "no rules configured" degradation is decided AFTER the call, from the returned FiscalYear. An
        // unconfigured NSubstitute call returns null and NREs inside ApplyStatutoryRulesAsync, so return the
        // empty result the real resolver produces when nothing is configured. (Zero-employee arms never reach
        // this, which is why it was not needed before.)
        var statutory = Substitute.For<IStatutoryDeductionResolver>();
        statutory.ResolveAsync(
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<StatutoryWageInput>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            // Default-constructed == the real resolver's "nothing configured" result: empty FiscalYear, no lines.
            .Returns(Result<StatutoryDeductions>.Success(new StatutoryDeductions()));

        var processor = new PayrollRunProcessor(
            db, tc,
            Substitute.For<IAttendancePayrollService>(),   // unlocked period → never called
            Substitute.For<IPayrollNotificationService>(),
            statutory,
            adjustments,
            Substitute.For<IPayrollSlipCleaner>(),          // no-op replace-clean (0 slips)
            Substitute.For<IPayrollAuditLogger>(),
            NullLogger<PayrollRunProcessor>.Instance);
        return (db, processor);
    }

    /// <summary>
    /// Seeds ONE payable employee: an Active employee plus a current salary component, which is the minimum
    /// <c>ProcessAsync</c> needs to actually emit a slip (an employee with no active salary assignment is
    /// SKIPPED, which is what every other arm in this file relies on to stay at zero slips).
    ///
    /// <para>No <c>StatutoryRules</c> are seeded, so the statutory gate stays closed and the substituted
    /// resolver is never called.</para>
    /// </summary>
    private async Task SeedPayableEmployeeAsync(Guid tenantId)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        await using var db = NewDb(tc);

        var employeeId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        // Employee.DepartmentId / JobTitleId are non-nullable AND FK-constrained on Postgres, so the parent
        // rows are mandatory — a default Guid fails with 23503 rather than being quietly accepted.
        var departmentId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();

        db.Departments.Add(new Department
        {
            Id = departmentId, TenantId = tenantId, Name = "Engineering", IsActive = true,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = tenantId, TitleName = "Engineer", IsActive = true,
        });

        db.Employees.Add(new Employee
        {
            Id = employeeId,
            TenantId = tenantId,
            EmployeeNo = "EMP-0001",
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = $"ada-{Guid.NewGuid():N}@acme.test",
            DateOfJoining = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DepartmentId = departmentId,
            JobTitleId = jobTitleId,
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
        });

        db.SalaryComponents.Add(new SalaryComponent
        {
            Id = componentId,
            TenantId = tenantId,
            Name = "Basic",
            Code = "BASIC",
            Type = SalaryComponentType.Earning,
            CalculationMethod = CalculationMethod.Fixed,
            IsTaxable = true,
            IsStatutory = false,
            IsActive = true,
            ProcessingOrder = 1,
        });

        db.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            SalaryStructureId = Guid.NewGuid(), // no FK constraint on this column
            SalaryComponentId = componentId,
            AnnualAmount = 1_200_000m,
            MonthlyAmount = 100_000m,
            EffectiveFrom = new DateOnly(2020, 1, 1),
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Opens a SEPARATE raw session and holds the run's advisory lock (the SAME key production uses,
    /// via <see cref="PayrollRunProcessLock.LockKey"/>). Dispose releases it by closing the session.</summary>
    private async Task<NpgsqlConnection> HoldRunLockAsync(Guid runId)
    {
        var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_advisory_lock(hashtext(@key))";
        var p = cmd.CreateParameter();
        p.ParameterName = "key";
        p.Value = PayrollRunProcessLock.LockKey(runId);
        cmd.Parameters.Add(p);
        await cmd.ExecuteScalarAsync();
        return conn;
    }

    private static string Sub() => "df61-" + Guid.NewGuid().ToString("N")[..8];

    // ═══════════════════════════ (A) DF-61-pg: reconcile marker ═══════════════════════════

    // A stale ReviewPending reprocess marker IS re-enqueued, and the reset COMMITS (survives a fresh reload).
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_reenqueues_a_stale_rerun_and_the_marker_reset_commits_on_postgres()
    {
        var scheduler = Substitute.For<IPayrollRunJobScheduler>();
        var provider = BuildReconcileProvider(scheduler);
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(tenantId, Sub(), TenantStatus.Active,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: Aged);

        await RunSweepAsync(provider);

        scheduler.Received(1).Enqueue(tenantId, Arg.Any<string>(), runId);

        // Proven from a FRESH DbContext — the marker reset was actually COMMITTED, not just change-tracked.
        await using var fresh = NewDb(new FixedTenantContext { TenantId = tenantId });
        (await fresh.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId))
            .ReprocessRequestedAt.Should().Be(NowUtc,
                "the marker is reset to now on re-enqueue so the next sweep won't re-fire for another threshold");
    }

    // A FRESH marker (inside the fast-path window) is NOT re-enqueued.
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_ignores_a_reviewpending_run_with_a_fresh_marker_on_postgres()
    {
        var scheduler = Substitute.For<IPayrollRunJobScheduler>();
        var provider = BuildReconcileProvider(scheduler);
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(tenantId, Sub(), TenantStatus.Active,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: Fresh);

        await RunSweepAsync(provider);

        scheduler.DidNotReceive().Enqueue(Arg.Any<Guid>(), Arg.Any<string>(), runId);
    }

    // A NULL marker (correctly-processed ReviewPending run) is NOT re-enqueued.
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_ignores_a_reviewpending_run_with_no_marker_on_postgres()
    {
        var scheduler = Substitute.For<IPayrollRunJobScheduler>();
        var provider = BuildReconcileProvider(scheduler);
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(tenantId, Sub(), TenantStatus.Active,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: null);

        await RunSweepAsync(provider);

        scheduler.DidNotReceive().Enqueue(Arg.Any<Guid>(), Arg.Any<string>(), runId);
    }

    // A Processing run with a stale marker is NOT re-enqueued (the reprocess is in-flight — no race).
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_never_reenqueues_a_processing_run_even_with_a_stale_marker_on_postgres()
    {
        var scheduler = Substitute.For<IPayrollRunJobScheduler>();
        var provider = BuildReconcileProvider(scheduler);
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(tenantId, Sub(), TenantStatus.Active,
            PayrollRunStatus.Processing, Aged, reprocessRequestedAt: Aged);

        await RunSweepAsync(provider);

        scheduler.DidNotReceive().Enqueue(Arg.Any<Guid>(), Arg.Any<string>(), runId);
    }

    // The rerun path is tenant-scoped + respects tenant activeness (mirrors the InMemory arm on real PG).
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_rerun_path_isolates_tenants_and_skips_inactive_on_postgres()
    {
        var scheduler = Substitute.For<IPayrollRunJobScheduler>();
        var provider = BuildReconcileProvider(scheduler);
        var active = Guid.NewGuid();
        var suspended = Guid.NewGuid();
        var activeRun = await SeedRunAsync(active, Sub(), TenantStatus.Active,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: Aged);
        var suspendedRun = await SeedRunAsync(suspended, Sub(), TenantStatus.Suspended,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: Aged);

        await RunSweepAsync(provider);

        scheduler.Received(1).Enqueue(active, Arg.Any<string>(), activeRun);
        scheduler.DidNotReceive().Enqueue(suspended, Arg.Any<string>(), suspendedRun);
    }

    // ═══════════════════════════ (B) DF-61-conc: per-runId interlock ═══════════════════════════

    // The interlock: while another session holds the run's advisory lock, ProcessAsync NO-OPs (does not
    // replace-clean / re-process) — proving two concurrent ProcessAsync on the SAME run cannot both run.
    [Fact]
    [Trait("TC", "TC-PAY-003-17")]
    public async Task ProcessAsync_noops_when_another_session_holds_the_run_lock_on_postgres()
    {
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(tenantId, Sub(), TenantStatus.Active, PayrollRunStatus.Queued, Aged);

        // Hold the SAME per-runId advisory lock from a separate session (stands in for the concurrent worker).
        await using var holder = await HoldRunLockAsync(runId);

        var (db, processor) = NewProcessor(tenantId);
        await using (db)
        {
            var result = await processor.ProcessAsync(runId);
            result.IsSuccess.Should().BeTrue("a contended lock is a clean no-op, never a throw (no Hangfire retry storm)");
        }

        // The run was NOT touched — still Queued, never advanced to Processing/ReviewPending.
        await using var fresh = NewDb(new FixedTenantContext { TenantId = tenantId });
        (await fresh.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId))
            .Status.Should().Be(PayrollRunStatus.Queued, "the lock winner does the processing; the loser no-ops");
        (await fresh.PayrollSlips.CountAsync(s => s.PayrollRunId == runId)).Should().Be(0);
    }

    // Once the lock is released, the SAME run processes normally (the interlock is not a permanent block).
    [Fact]
    [Trait("TC", "TC-PAY-003-17")]
    public async Task ProcessAsync_proceeds_after_the_run_lock_is_released_on_postgres()
    {
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(tenantId, Sub(), TenantStatus.Active, PayrollRunStatus.Queued, Aged);

        var holder = await HoldRunLockAsync(runId);
        await holder.DisposeAsync(); // release before processing

        var (db, processor) = NewProcessor(tenantId);
        await using (db)
            (await processor.ProcessAsync(runId)).IsSuccess.Should().BeTrue();

        await using var fresh = NewDb(new FixedTenantContext { TenantId = tenantId });
        (await fresh.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId))
            .Status.Should().Be(PayrollRunStatus.ReviewPending, "with the lock free, the run processes to completion");
    }

    // Per-runId scoping: holding run A's lock does NOT serialize a DIFFERENT run B (no false serialization).
    [Fact]
    [Trait("TC", "TC-PAY-003-17")]
    public async Task ProcessAsync_for_a_different_run_is_not_blocked_by_another_runs_lock_on_postgres()
    {
        var tenantId = Guid.NewGuid();
        var sub = Sub();
        var runA = await SeedRunAsync(tenantId, sub, TenantStatus.Active, PayrollRunStatus.Queued, Aged, payMonth: 5);
        // Distinct period so both runs coexist (ix_payroll_run_one_active_per_period allows one active run per tenant/year/month).
        var runB = await SeedRunAsync(tenantId, sub, TenantStatus.Active, PayrollRunStatus.Queued, Aged, payMonth: 6);

        // Different keys → holding A's lock must not block B.
        PayrollRunProcessLock.LockKey(runA).Should().NotBe(PayrollRunProcessLock.LockKey(runB));

        await using var holderA = await HoldRunLockAsync(runA);

        var (db, processor) = NewProcessor(tenantId);
        await using (db)
            (await processor.ProcessAsync(runB)).IsSuccess.Should().BeTrue();

        await using var fresh = NewDb(new FixedTenantContext { TenantId = tenantId });
        (await fresh.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runB))
            .Status.Should().Be(PayrollRunStatus.ReviewPending, "run B has a different lock key and is not serialized behind run A");
        (await fresh.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runA))
            .Status.Should().Be(PayrollRunStatus.Queued, "run A is still locked/untouched");
    }

    // Retry-after-completion edge: a ReviewPending run whose reprocess is ALREADY SATISFIED (NULL marker) no-ops
    // instead of re-processing (which would re-duplicate slips). CompletedAt is the sentinel: the process path
    // overwrites it, the skip path leaves it untouched.
    [Fact]
    [Trait("TC", "TC-PAY-003-17")]
    public async Task ProcessAsync_skips_a_reviewpending_run_whose_reprocess_is_already_satisfied_on_postgres()
    {
        var tenantId = Guid.NewGuid();
        var sentinel = DateTime.SpecifyKind(new DateTime(2026, 1, 1, 0, 0, 0), DateTimeKind.Utc);
        var runId = await SeedRunAsync(tenantId, Sub(), TenantStatus.Active,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: null, completedAt: sentinel, totalGross: 12_345m);

        var (db, processor) = NewProcessor(tenantId);
        await using (db)
            (await processor.ProcessAsync(runId)).IsSuccess.Should().BeTrue();

        await using var fresh = NewDb(new FixedTenantContext { TenantId = tenantId });
        var run = await fresh.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        run.Status.Should().Be(PayrollRunStatus.ReviewPending);
        run.CompletedAt.Should().Be(sentinel, "the already-satisfied run was skipped, so its completion was not overwritten");
        run.TotalGross.Should().Be(12_345m, "the skip path never re-ran the compute (which would reset totals to 0)");
    }

    // The opposite of the skip edge: a ReviewPending run WITH a marker set IS a due reprocess — ProcessAsync
    // proceeds, clears the marker, and the clear COMMITS on Postgres.
    [Fact]
    [Trait("TC", "TC-PAY-003-17")]
    public async Task ProcessAsync_processes_a_marked_rerun_and_clears_the_marker_on_postgres()
    {
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(tenantId, Sub(), TenantStatus.Active,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: Aged, totalGross: 12_345m);

        var (db, processor) = NewProcessor(tenantId);
        await using (db)
            (await processor.ProcessAsync(runId)).IsSuccess.Should().BeTrue();

        await using var fresh = NewDb(new FixedTenantContext { TenantId = tenantId });
        var run = await fresh.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        run.ReprocessRequestedAt.Should().BeNull("a completed reprocess clears the marker so the sweep no longer treats it as stranded");
        run.TotalGross.Should().Be(0m, "the marked rerun was actually re-processed (0 employees → totals reset to 0)");
    }

    // ═══════════ (C) DF-61-conc-slip: the interlock on a run that actually produces slips ═══════════
    //
    // Every other interlock arm drives a ZERO-employee run, so the loser's no-op is proven via
    // "Status == Queued + 0 slips" — a DEGENERATE count that is also what you would see if slips were never
    // produced at all. The real failure mode the lock exists to prevent is "two concurrent ProcessAsync →
    // DUPLICATE ReviewPending slips", which a zero-slip run structurally cannot exhibit.
    //
    // This arm seeds a payable employee so the winner genuinely emits a slip, then contends the lock and
    // asserts the count does not grow and no employee gains a second slip for the same run.
    //
    // The substituted IPayrollSlipCleaner is load-bearing here in the test's favour: because it does NOT
    // remove prior slips, a loser that wrongly proceeded would ADD a duplicate rather than replace-clean it
    // away — so the duplication would be visible rather than silently masked.

    [Fact]
    [Trait("TC", "TC-PAY-003-17")]
    public async Task ProcessAsync_contended_does_not_duplicate_REAL_slips_on_postgres()
    {
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(tenantId, Sub(), TenantStatus.Active,
            PayrollRunStatus.Queued, Aged);
        await SeedPayableEmployeeAsync(tenantId);

        // Winner: processes the run for real and emits the slip.
        var (winnerDb, winner) = NewProcessor(tenantId);
        await using (winnerDb)
            (await winner.ProcessAsync(runId)).IsSuccess.Should().BeTrue();

        await using (var afterWinner = NewDb(new FixedTenantContext { TenantId = tenantId }))
        {
            var produced = await afterWinner.PayrollSlips.AsNoTracking()
                .CountAsync(s => s.PayrollRunId == runId);
            produced.Should().Be(1,
                "the arm is only meaningful if the winner actually produced a slip — a zero-slip run would " +
                "make the duplication assertion below vacuous");
        }

        // Mark a reprocess as due, so the contended caller would otherwise have real work to do.
        await using (var mark = NewDb(new FixedTenantContext { TenantId = tenantId }))
        {
            var run = await mark.PayrollRuns.SingleAsync(r => r.Id == runId);
            run.ReprocessRequestedAt = Aged;
            await mark.SaveChangesAsync();
        }

        // Loser: another session holds the run's lock, so this call must no-op.
        await using var holder = await HoldRunLockAsync(runId);
        var (loserDb, loser) = NewProcessor(tenantId);
        await using (loserDb)
            (await loser.ProcessAsync(runId)).IsSuccess.Should().BeTrue("the loser no-ops, it never throws");

        await using var fresh = NewDb(new FixedTenantContext { TenantId = tenantId });
        var slips = await fresh.PayrollSlips.AsNoTracking()
            .Where(s => s.PayrollRunId == runId)
            .ToListAsync();

        slips.Should().HaveCount(1, "the contended caller must not re-emit slips for a run already processed");
        slips.Select(s => s.EmployeeId).Should().OnlyHaveUniqueItems(
            "no employee may hold two slips for the same run — that is the duplication the interlock exists to prevent");
    }

    // ═══════════════ (D) DF-61-conc-approval-race: reprocess vs. the approval workflow ═══════════════
    //
    // A reprocess enqueued while ReviewPending, then raced by an HR submit or approval before the worker ran,
    // used to sail straight through: it replace-cleaned the slips and reverted the run to ReviewPending,
    // silently un-submitting the approval. Decision D-d (2026-08-04) is refuse-and-tell.
    //
    // NOTE ON WHAT THESE ARMS CAN AND CANNOT PROVE. ProcessAsync reads the run, takes the interlock, then
    // RE-READS and re-guards; the race lives in that second window. There is no deterministic way to mutate
    // the row from a test between those two points in a single in-process call. What makes these arms
    // sufficient is that both sites now call the SAME GuardNonReprocessableStatus helper — the duplication of
    // the guard was the defect, so proving the helper refuses the right states proves both sites refuse them.
    // If someone re-inlines the checks, that shared-helper property is what breaks, not these assertions.

    [Fact]
    [Trait("TC", "TC-PAY-003-17")]
    public async Task ProcessAsync_refuses_to_reprocess_a_run_awaiting_approval_on_postgres()
    {
        var tenantId = Guid.NewGuid();
        var sentinel = DateTime.SpecifyKind(new DateTime(2026, 1, 1, 0, 0, 0), DateTimeKind.Utc);
        // Marker set: a reprocess is genuinely due — this is exactly the enqueue-then-submit race.
        var runId = await SeedRunAsync(tenantId, Sub(), TenantStatus.Active,
            PayrollRunStatus.AwaitingApproval, Aged, reprocessRequestedAt: Aged,
            completedAt: sentinel, totalGross: 12_345m);

        var (db, processor) = NewProcessor(tenantId);
        Result result;
        await using (db)
            result = await processor.ProcessAsync(runId);

        result.IsFailure.Should().BeTrue("an approval is in flight — reprocessing would un-submit it");
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("run_awaiting_approval");

        await using var fresh = NewDb(new FixedTenantContext { TenantId = tenantId });
        var run = await fresh.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        run.Status.Should().Be(PayrollRunStatus.AwaitingApproval, "the in-flight approval must survive untouched");
        run.TotalGross.Should().Be(12_345m, "refusing must not re-run the compute (which would reset totals to 0)");
        run.CompletedAt.Should().Be(sentinel);
    }

    [Fact]
    [Trait("TC", "TC-PAY-003-17")]
    public async Task ProcessAsync_refuses_to_reprocess_an_approved_run_on_postgres()
    {
        // The sharper hazard: reprocessing here silently changes figures somebody already signed off on,
        // while the run still reads as Approved.
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(tenantId, Sub(), TenantStatus.Active,
            PayrollRunStatus.Approved, Aged, reprocessRequestedAt: Aged, totalGross: 12_345m);

        var (db, processor) = NewProcessor(tenantId);
        Result result;
        await using (db)
            result = await processor.ProcessAsync(runId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("run_approved");

        await using var fresh = NewDb(new FixedTenantContext { TenantId = tenantId });
        var run = await fresh.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        run.Status.Should().Be(PayrollRunStatus.Approved, "an approved run must not be silently reverted");
        run.TotalGross.Should().Be(12_345m, "the approved figures must be exactly what was signed off");
    }

    [Fact]
    [Trait("TC", "TC-PAY-003-17")]
    public async Task ProcessAsync_refuses_when_the_run_is_approved_BETWEEN_the_pre_lock_read_and_the_reload()
    {
        // THE ACTUAL RACE — and the only arm that covers the POST-LOCK re-read guard.
        //
        // The other refusal arms seed the run as AwaitingApproval/Approved up front, so the PRE-lock guard
        // catches them and the post-lock site is never reached: deleting the post-lock guard leaves them all
        // green. Here the run is genuinely ReviewPending with a due reprocess when ProcessAsync starts (the
        // pre-lock guard passes), and an approval lands from another connection before the reload — so only
        // the post-lock guard can refuse it. This is the enqueue-then-approve sequence from production.
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(tenantId, Sub(), TenantStatus.Active,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: Aged, totalGross: 12_345m);
        await SeedPayableEmployeeAsync(tenantId);

        var flipper = new ApproveBetweenReadsInterceptor(
            _postgres.GetConnectionString(), runId, PayrollRunStatus.Approved);

        var (db, processor) = NewProcessor(tenantId, flipper);
        Result result;
        await using (db)
            result = await processor.ProcessAsync(runId);

        flipper.DidFlip.Should().BeTrue(
            "the seam must actually have fired — otherwise this arm silently degrades into a plain " +
            "ReviewPending reprocess and proves nothing about the race");

        result.IsFailure.Should().BeTrue("the approval landed before the reload, so the reprocess must refuse");
        result.ErrorCode.Should().Be("run_approved");

        await using var fresh = NewDb(new FixedTenantContext { TenantId = tenantId });
        var run = await fresh.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        run.Status.Should().Be(PayrollRunStatus.Approved,
            "the run must NOT be silently reverted to ReviewPending — that reversion IS the bug");
        run.TotalGross.Should().Be(12_345m, "the approved figures must survive untouched");

        var slips = await fresh.PayrollSlips.AsNoTracking().CountAsync(s => s.PayrollRunId == runId);
        slips.Should().Be(0, "the refused reprocess must not have replace-cleaned or re-emitted any slip");
    }

    [Fact]
    [Trait("TC", "TC-PAY-003-17")]
    public async Task ProcessAsync_STILL_processes_a_rejected_run_on_postgres()
    {
        // The deliberate carve-out, and the reason the guard is not simply "everything past ReviewPending":
        // a Rejected run is legitimately re-runnable — HR corrects it and re-submits, creating a new workflow
        // instance. Blocking it would break the correction loop the approval workflow exists to support.
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(tenantId, Sub(), TenantStatus.Active,
            PayrollRunStatus.Rejected, Aged, reprocessRequestedAt: Aged, totalGross: 12_345m);

        var (db, processor) = NewProcessor(tenantId);
        Result result;
        await using (db)
            result = await processor.ProcessAsync(runId);

        result.IsSuccess.Should().BeTrue(
            "a rejected run must stay re-runnable so HR can correct and re-submit — {0}", result.Error);

        await using var fresh = NewDb(new FixedTenantContext { TenantId = tenantId });
        var run = await fresh.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        run.Status.Should().Be(PayrollRunStatus.ReviewPending, "the corrected run returns to review");
        run.TotalGross.Should().Be(0m, "it was genuinely re-processed (0 employees → totals reset to 0)");
        run.ReprocessRequestedAt.Should().BeNull("the completed reprocess cleared the marker");
    }
}
