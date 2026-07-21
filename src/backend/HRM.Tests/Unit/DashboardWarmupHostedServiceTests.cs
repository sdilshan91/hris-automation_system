// ============================================================================
// DF-50 (Part 1): DashboardWarmupHostedService smoke test. The warm body must run
// WITHOUT a resolved tenant (it primes with IgnoreQueryFilters) and must NEVER throw
// — a warmup failure can only forfeit the cold-start win, never crash the host.
// ============================================================================

using FluentAssertions;
using HRM.Api.HostedServices;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Unit;

public sealed class DashboardWarmupHostedServiceTests
{
    // Unresolved tenant context (IsResolved false) — proves the warm reads don't need a resolved tenant.
    private sealed class UnresolvedTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public string Subdomain => string.Empty;
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => false;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status, string? plan = null,
            IReadOnlyCollection<string>? enabledModules = null, string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    [Fact]
    [Trait("TC", "TC-RPT-005-50")]
    public async Task WarmAsync_WithNoResolvedTenant_PrimesEveryHotEntity_WithoutThrowing()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, UnresolvedTenantContext>();
        services.AddScoped<AppDbContext>(sp => new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options,
            sp.GetRequiredService<ITenantContext>()));
        var provider = services.BuildServiceProvider();

        var warmup = new DashboardWarmupHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<DashboardWarmupHostedService>.Instance);

        var act = async () => await warmup.WarmAsync(CancellationToken.None);

        await act.Should().NotThrowAsync("warmup is best-effort and must never surface an error to the host");
    }

    [Fact]
    [Trait("TC", "TC-RPT-005-50")]
    public async Task StartAsync_NeverThrows_EvenWhenTheDetachedWarmFaults()
    {
        // No AppDbContext registered → the detached warm task faults internally, but StartAsync must return
        // and swallow it (the fault is logged inside WarmAsync, never rethrown out of the host).
        var provider = new ServiceCollection().BuildServiceProvider();
        var warmup = new DashboardWarmupHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<DashboardWarmupHostedService>.Instance);

        var act = async () => await warmup.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// A scope factory spy: counts scope creations, signals when the warm task ENTERS CreateScope, and can BLOCK
    /// there on a gate — so a test can prove StartAsync returns while warm is still in-flight (fire-and-forget).
    /// </summary>
    private sealed class SpyScopeFactory : IServiceScopeFactory, IDisposable
    {
        public int CreateScopeCount;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Gate = new(initialState: true); // set = pass-through; Reset() = block

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref CreateScopeCount);
            Entered.TrySetResult();
            Gate.Wait();
            return new NoopScope();
        }

        public void Dispose() => Gate.Dispose();

        private sealed class NoopScope : IServiceScope
        {
            // Empty provider → WarmAsync's GetRequiredService<AppDbContext> throws, is caught + swallowed. Fine.
            public IServiceProvider ServiceProvider { get; } = new ServiceCollection().BuildServiceProvider();
            public void Dispose() { }
        }
    }

    [Fact]
    [Trait("TC", "TC-RPT-005-50")]
    public async Task StartAsync_IsFireAndForget_ReturnsWhileTheWarmTaskIsStillRunning()
    {
        // Prove the detach: StartAsync must NOT await WarmAsync. The spy blocks the warm task inside CreateScope;
        // StartAsync must still have completed (returned Task.CompletedTask) while the warm is stuck at the gate.
        using var spy = new SpyScopeFactory();
        spy.Gate.Reset(); // make the warm task block once it reaches CreateScope
        var warmup = new DashboardWarmupHostedService(
            spy, new ConfigurationBuilder().Build(), NullLogger<DashboardWarmupHostedService>.Instance);

        try
        {
            var startTask = warmup.StartAsync(CancellationToken.None);

            startTask.IsCompleted.Should().BeTrue("StartAsync must return immediately, not await the detached warm");
            // The background warm reached CreateScope and is now blocked — while StartAsync has already returned.
            await spy.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            spy.Gate.Set(); // release the blocked warm thread so it can fault+swallow and exit
        }
    }

    [Fact]
    [Trait("TC", "TC-RPT-005-50")]
    public async Task StartAsync_WhenWarmupDisabled_NeverCreatesAScope()
    {
        // Dashboard:WarmupEnabled=false → the skip returns BEFORE scheduling the warm Task.Run, so no scope is
        // ever created. (The guard is synchronous, so CreateScopeCount is deterministically 0.)
        using var spy = new SpyScopeFactory();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dashboard:WarmupEnabled"] = "false" })
            .Build();
        var warmup = new DashboardWarmupHostedService(
            spy, config, NullLogger<DashboardWarmupHostedService>.Instance);

        await warmup.StartAsync(CancellationToken.None);
        await Task.Yield(); // give any (erroneously) scheduled warm task a chance to run

        spy.CreateScopeCount.Should().Be(0, "warmup is disabled, so the warm task must never run");
    }
}
