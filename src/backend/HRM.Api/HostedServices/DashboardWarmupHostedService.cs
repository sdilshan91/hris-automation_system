using System.Diagnostics;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Api.HostedServices;

/// <summary>
/// DF-50 (Part 1) — kills the dashboard COLD-START outlier (~25s on the very first request: EF model build +
/// per-entity query-plan compile + cold connection pool / caches). On startup it fires ONE trivial read per hot
/// entity in the background so the first real request pays warm latency (~p95 193ms), not the model-build tax.
///
/// <para><b>Non-blocking + fail-safe.</b> Warmup runs on a detached <see cref="Task.Run(Action)"/> so it never
/// delays host startup, and the whole thing is wrapped so a failure is logged and swallowed — priming must NEVER
/// crash the app (mirrors <c>DbInitializer</c>'s Development-tolerant stance). The reads use
/// <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/> so NO resolved tenant is required — the
/// point is to force the model/plan/pool warm, and the FIRST such query already pays the dominant model-build
/// cost. Migrations have already run (DbInitializer is awaited before the host starts), but we stay defensive.</para>
///
/// <para>Gate: <c>Dashboard:WarmupEnabled</c> (default true) disables it entirely (e.g. for a fast test host).</para>
/// </summary>
public sealed class DashboardWarmupHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DashboardWarmupHostedService> _logger;

    public DashboardWarmupHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DashboardWarmupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue("Dashboard:WarmupEnabled", true))
        {
            _logger.LogInformation("Dashboard warmup disabled (Dashboard:WarmupEnabled=false); skipping.");
            return Task.CompletedTask;
        }

        // Detach: do NOT block host startup. Failures are logged and swallowed inside WarmAsync.
        _ = Task.Run(() => WarmAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// The priming body — public so it can be exercised directly by a smoke test with a test scope factory.
    /// Runs one trivial tenant-agnostic read per hot entity. Never throws: any error is logged as a warning.
    /// </summary>
    public async Task WarmAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _logger.LogInformation("Dashboard warmup starting (priming EF model + query plans + connection pool).");

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // The FIRST of these forces the whole EF model build; each subsequent one compiles that entity's plan
            // and primes the pooled connection. IgnoreQueryFilters => no resolved tenant needed (read-only prime).
            await db.Employees.IgnoreQueryFilters().Take(1).ToListAsync(ct);
            await db.LeaveRequests.IgnoreQueryFilters().Take(1).ToListAsync(ct);
            await db.AttendanceLogs.IgnoreQueryFilters().Take(1).ToListAsync(ct);
            await db.LeaveLedgerEntries.IgnoreQueryFilters().Take(1).ToListAsync(ct);
            await db.Set<OnboardingChecklistInstance>().IgnoreQueryFilters().Take(1).ToListAsync(ct);
            await db.PayrollSlips.IgnoreQueryFilters().Take(1).ToListAsync(ct);
            await db.Holidays.IgnoreQueryFilters().Take(1).ToListAsync(ct);
            await db.LeaveTypes.IgnoreQueryFilters().Take(1).ToListAsync(ct);
            await db.Departments.IgnoreQueryFilters().Take(1).ToListAsync(ct);

            sw.Stop();
            _logger.LogInformation("Dashboard warmup complete in {ElapsedMs}ms.", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            // NEVER rethrow — warmup is best-effort; a failure just means the first real request pays cold cost.
            _logger.LogWarning(ex, "Dashboard warmup failed after {ElapsedMs}ms; continuing (first request will "
                + "pay the cold-start cost).", sw.ElapsedMilliseconds);
        }
    }
}
