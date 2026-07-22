// ============================================================================
// DF-58 — PayrollRunReconcileJob (durability backstop sweep, sibling of DF-4).
//
// PayrollRunService.InitiateAsync commits a run in Queued then best-effort post-commit Enqueues the processor.
// A lost enqueue strands the run in Queued forever AND (BR-1 one-active-per-period) blocks any new initiate for
// that period. This sweep re-enqueues stranded Queued runs — but ONLY Queued+aged, NEVER Processing/ReviewPending
// (PayrollRun has no concurrency token; racing a live worker corrupts slips). These tests drive the real
// job.RunAsync over a real DI graph (real TenantJobRunner, RLS off) on InMemory-through-real-EF, proving:
//   1. a stranded (aged) Queued run is re-enqueued,
//   2. a freshly-Queued run (within the fast-path window) is NOT,
//   3+4. Processing / ReviewPending runs are NEVER re-enqueued (the safety guardrail),
//   5. among mixed statuses all aged, ONLY the Queued one is re-enqueued (mutation-critical),
//   6. the sweep is tenant-scoped and skips inactive tenants.
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class PayrollRunReconcileJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeProvider FixedClock = new FixedTimeProvider(Now);
    // StaleThreshold is 10 min: aged = comfortably past it; fresh = within the fast-path window.
    private static readonly DateTime Aged = Now.UtcDateTime.AddMinutes(-20);
    private static readonly DateTime Fresh = Now.UtcDateTime.AddMinutes(-2);

    private readonly IPayrollRunJobScheduler _scheduler = Substitute.For<IPayrollRunJobScheduler>();

    // ── 1. A stranded (aged) Queued run is re-enqueued ───────────────────────
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_reenqueues_a_stranded_queued_run()
    {
        var provider = BuildProvider(out var dbName);
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active, PayrollRunStatus.Queued, Aged);

        await RunSweepAsync(provider);

        _scheduler.Received(1).Enqueue(tenantId, Arg.Any<string>(), runId);
    }

    // ── 2. A freshly-Queued run (still in the fast-path window) is NOT ────────
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_ignores_a_freshly_queued_run()
    {
        var provider = BuildProvider(out var dbName);
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active, PayrollRunStatus.Queued, Fresh);

        await RunSweepAsync(provider);

        _scheduler.DidNotReceive().Enqueue(Arg.Any<Guid>(), Arg.Any<string>(), runId);
    }

    // ── 3+4. Processing / ReviewPending are NEVER re-enqueued (the safety guardrail) ──
    [Theory]
    [Trait("TC", "TC-PAY-003-16")]
    [InlineData(PayrollRunStatus.Processing)]
    [InlineData(PayrollRunStatus.ReviewPending)]
    public async Task Sweep_never_reenqueues_a_live_or_reviewed_run_even_when_aged(PayrollRunStatus status)
    {
        var provider = BuildProvider(out var dbName);
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active, status, Aged);

        await RunSweepAsync(provider);

        _scheduler.DidNotReceive().Enqueue(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>());
    }

    // ── 5. Mixed statuses, all aged → ONLY the Queued one (mutation-critical) ─
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_only_reenqueues_the_queued_run_among_mixed_aged_statuses()
    {
        var provider = BuildProvider(out var dbName);
        var tenantId = Guid.NewGuid();
        var queuedAged = await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active, PayrollRunStatus.Queued, Aged);
        await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active, PayrollRunStatus.Processing, Aged);
        await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active, PayrollRunStatus.ReviewPending, Aged);
        await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active, PayrollRunStatus.Queued, Fresh);

        await RunSweepAsync(provider);

        _scheduler.Received(1).Enqueue(tenantId, Arg.Any<string>(), queuedAged);
        // Exactly one enqueue total — nothing else slipped through.
        _scheduler.Received(1).Enqueue(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>());
    }

    // ── 6. Tenant-scoped + skips inactive ────────────────────────────────────
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_isolates_tenants_and_skips_inactive()
    {
        var provider = BuildProvider(out var dbName);
        var active = Guid.NewGuid();
        var suspended = Guid.NewGuid();
        var activeRun = await SeedRunAsync(dbName, active, "a", TenantStatus.Active, PayrollRunStatus.Queued, Aged);
        var suspendedRun = await SeedRunAsync(dbName, suspended, "s", TenantStatus.Suspended, PayrollRunStatus.Queued, Aged);

        await RunSweepAsync(provider);

        _scheduler.Received(1).Enqueue(active, Arg.Any<string>(), activeRun);
        _scheduler.DidNotReceive().Enqueue(suspended, Arg.Any<string>(), suspendedRun);
    }

    // ── DF-61: a stale ReviewPending run whose reprocess was REQUESTED (marker set) IS re-enqueued ──
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_reenqueues_a_reviewpending_run_with_a_stale_reprocess_marker_DF61()
    {
        var provider = BuildProvider(out var dbName);
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: Aged);

        await RunSweepAsync(provider);

        _scheduler.Received(1).Enqueue(tenantId, Arg.Any<string>(), runId);
        // The marker is reset to now so the next sweep won't re-fire for another StaleThreshold.
        await using var db = ReadDb(dbName, tenantId);
        db.PayrollRuns.Single(r => r.Id == runId).ReprocessRequestedAt.Should().Be(Now.UtcDateTime,
            "the marker is reset on re-enqueue to bound re-fires while the reprocess job gets a chance to run");
    }

    // ── DF-61: a ReviewPending run with a FRESH marker is NOT re-enqueued (not stale yet) ──
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_ignores_a_reviewpending_run_with_a_fresh_reprocess_marker_DF61()
    {
        var provider = BuildProvider(out var dbName);
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: Fresh);

        await RunSweepAsync(provider);

        _scheduler.DidNotReceive().Enqueue(Arg.Any<Guid>(), Arg.Any<string>(), runId);
    }

    // ── DF-61: a ReviewPending run with NO marker is NOT re-enqueued (correctly-processed, not a stranded rerun) ──
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_ignores_a_reviewpending_run_with_no_reprocess_marker_DF61()
    {
        var provider = BuildProvider(out var dbName);
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: null);

        await RunSweepAsync(provider);

        _scheduler.DidNotReceive().Enqueue(Arg.Any<Guid>(), Arg.Any<string>(), runId);
    }

    // ── DF-61: a Processing run with a stale marker is NOT re-enqueued (the reprocess is in-flight, no race) ──
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_never_reenqueues_a_processing_run_even_with_a_stale_marker_DF61()
    {
        var provider = BuildProvider(out var dbName);
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active,
            PayrollRunStatus.Processing, Aged, reprocessRequestedAt: Aged);

        await RunSweepAsync(provider);

        _scheduler.DidNotReceive().Enqueue(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>());
    }

    // ── DF-61: the Queued (DF-58) and stale-rerun (DF-61) paths compose in one sweep — exactly two enqueues ──
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_reenqueues_both_a_stranded_queued_and_a_stale_rerun_in_one_pass_DF61()
    {
        var provider = BuildProvider(out var dbName);
        var tenantId = Guid.NewGuid();
        var queuedAged = await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active, PayrollRunStatus.Queued, Aged);
        var staleRerun = await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: Aged);

        await RunSweepAsync(provider);

        _scheduler.Received(1).Enqueue(tenantId, Arg.Any<string>(), queuedAged);
        _scheduler.Received(1).Enqueue(tenantId, Arg.Any<string>(), staleRerun);
        // Exactly two enqueues total — nothing else slipped through.
        _scheduler.Received(2).Enqueue(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid>());
    }

    // ── DF-61: the marker reset bounds re-fires — a SECOND sweep within the threshold does NOT re-enqueue ──
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_run_twice_within_threshold_reenqueues_a_stale_rerun_only_once_DF61()
    {
        var provider = BuildProvider(out var dbName);
        var tenantId = Guid.NewGuid();
        var runId = await SeedRunAsync(dbName, tenantId, "acme", TenantStatus.Active,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: Aged);

        await RunSweepAsync(provider);   // 1st: marker Aged < cutoff -> enqueue + reset marker to Now.
        await RunSweepAsync(provider);   // 2nd: marker now == Now (not < cutoff) -> skipped (same FixedClock).

        _scheduler.Received(1).Enqueue(tenantId, Arg.Any<string>(), runId);
    }

    // ── DF-61: the rerun path is tenant-scoped + respects tenant activeness (mirrors the Queued-path arm 6) ──
    [Fact]
    [Trait("TC", "TC-PAY-003-16")]
    public async Task Sweep_rerun_path_isolates_tenants_and_skips_inactive_DF61()
    {
        var provider = BuildProvider(out var dbName);
        var active = Guid.NewGuid();
        var suspended = Guid.NewGuid();
        var activeRun = await SeedRunAsync(dbName, active, "a", TenantStatus.Active,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: Aged);
        var suspendedRun = await SeedRunAsync(dbName, suspended, "s", TenantStatus.Suspended,
            PayrollRunStatus.ReviewPending, Aged, reprocessRequestedAt: Aged);

        await RunSweepAsync(provider);

        _scheduler.Received(1).Enqueue(active, Arg.Any<string>(), activeRun);
        _scheduler.DidNotReceive().Enqueue(suspended, Arg.Any<string>(), suspendedRun);
    }

    // ══════════════════════════════ Fixtures ══════════════════════════════

    private ServiceProvider BuildProvider(out string dbName)
    {
        dbName = Guid.NewGuid().ToString();
        var name = dbName;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // Rls:Enabled defaults false
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser>(_ =>
        {
            var cu = Substitute.For<ICurrentUser>();
            cu.IsAuthenticated.Returns(false);
            return cu;
        });
        services.AddScoped<ITenantJobRunner, TenantJobRunner>();
        services.AddSingleton(_scheduler);
        return services.BuildServiceProvider();
    }

    private async Task RunSweepAsync(ServiceProvider provider) =>
        await new PayrollRunReconcileJob(provider.GetRequiredService<IServiceScopeFactory>(), FixedClock).RunAsync();

    private static async Task<Guid> SeedRunAsync(
        string dbName, Guid tenantId, string subdomain, TenantStatus tenantStatus,
        PayrollRunStatus runStatus, DateTime initiatedAt, DateTime? reprocessRequestedAt = null)
    {
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, subdomain, TenantStatus.Active); // Active context so the write is admitted
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options, ctx);

        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId))
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, Subdomain = subdomain, Name = subdomain, Status = tenantStatus, FiscalYearStartMonth = 1,
            });

        var runId = Guid.NewGuid();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId,
            TenantId = tenantId,
            Status = runStatus,
            InitiatedAt = initiatedAt,
            ReprocessRequestedAt = reprocessRequestedAt,
        });
        await db.SaveChangesAsync();
        return runId;
    }

    private static AppDbContext ReadDb(string dbName, Guid tenantId)
    {
        var ctx = new TenantContext();
        ctx.SetTenant(tenantId, "read", TenantStatus.Active);
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options, ctx);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
