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
        PayrollRunStatus runStatus, DateTime initiatedAt)
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
        });
        await db.SaveChangesAsync();
        return runId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
