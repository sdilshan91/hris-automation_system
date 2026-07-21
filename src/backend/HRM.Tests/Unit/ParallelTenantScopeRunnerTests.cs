// ============================================================================
// DF-50: ParallelTenantScopeRunner — the fan-out helper behind the parallelized
// dashboard. These lock the runner's contract in isolation (no EF):
//   - each factory runs in its OWN scope with the parent tenant RE-SEEDED,
//   - results come back IN INPUT ORDER,
//   - concurrency is bounded by maxDegreeOfParallelism,
//   - a factory throwing PROPAGATES (documented choice — matches DashboardService's
//     existing "a widget failure fails the whole request" behaviour; there is no
//     per-widget swallow in the shipped code, so the runner does not add one).
// The cross-tenant DATA gate (that the re-seed actually scopes EF) is proven
// end-to-end in DashboardIntegrationTests.Dashboard_UnderParallelFanOut_*.
// ============================================================================

using System.Collections.Concurrent;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Unit;

public sealed class ParallelTenantScopeRunnerTests
{
    // A scoped tenant context that starts UNRESOLVED — exactly like a fresh child scope in production.
    private sealed class RunnerTenantContext : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public string Subdomain { get; private set; } = string.Empty;
        public TenantStatus Status { get; private set; }
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext { get; private set; }
        public bool IsResolved { get; private set; }
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status, string? plan = null,
            IReadOnlyCollection<string>? enabledModules = null, string? logoUrl = null, string? primaryColor = null)
        { TenantId = tenantId; Subdomain = subdomain; Status = status; IsResolved = true; IsSystemContext = false; }
        public void SetSystemContext() { IsSystemContext = true; IsResolved = true; }
    }

    private static (IServiceScopeFactory Factory, ITenantContext Parent) Build(Guid parentTenantId)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, RunnerTenantContext>(); // fresh (unresolved) per child scope
        var provider = services.BuildServiceProvider();
        var parent = new RunnerTenantContext();
        parent.SetTenant(parentTenantId, "acme", TenantStatus.Active);
        return (provider.GetRequiredService<IServiceScopeFactory>(), parent);
    }

    [Fact]
    [Trait("TC", "TC-RPT-005-50")]
    public async Task RunAllAsync_ReturnsResultsInInputOrder_AndReSeedsTenantInEachChildScope()
    {
        var parentTenantId = Guid.NewGuid();
        var (factory, parent) = Build(parentTenantId);
        var runner = new ParallelTenantScopeRunner(factory, parent);

        var seenTenantIds = new ConcurrentBag<Guid>();
        var scopeInstances = new ConcurrentBag<ITenantContext>();

        var work = Enumerable.Range(0, 5)
            .Select<int, Func<IServiceProvider, CancellationToken, Task<int>>>(i => async (sp, ct) =>
            {
                var tc = sp.GetRequiredService<ITenantContext>();
                seenTenantIds.Add(tc.TenantId);
                scopeInstances.Add(tc);
                await Task.Yield();
                return i * 10;
            })
            .ToList();

        var results = await runner.RunAllAsync(work, maxDegreeOfParallelism: 4, CancellationToken.None);

        // Order preserved regardless of completion order.
        results.Should().Equal(0, 10, 20, 30, 40);
        // Every child scope was re-seeded to the parent tenant (the isolation gate).
        seenTenantIds.Should().OnlyContain(id => id == parentTenantId);
        seenTenantIds.Should().HaveCount(5);
        // Each item got a DISTINCT scope (distinct tenant-context instances).
        scopeInstances.Distinct().Should().HaveCount(5);
    }

    [Fact]
    [Trait("TC", "TC-RPT-005-50")]
    public async Task RunAllAsync_RespectsMaxDegreeOfParallelism()
    {
        var (factory, parent) = Build(Guid.NewGuid());
        var runner = new ParallelTenantScopeRunner(factory, parent);

        int current = 0;
        int maxObserved = 0;
        var lockObj = new object();

        var work = Enumerable.Range(0, 8)
            .Select<int, Func<IServiceProvider, CancellationToken, Task<int>>>(i => async (sp, ct) =>
            {
                lock (lockObj) { current++; maxObserved = Math.Max(maxObserved, current); }
                await Task.Delay(40, ct); // hold the slot long enough to force overlap
                lock (lockObj) { current--; }
                return i;
            })
            .ToList();

        await runner.RunAllAsync(work, maxDegreeOfParallelism: 3, CancellationToken.None);

        maxObserved.Should().BeLessThanOrEqualTo(3, "concurrency must be bounded by maxDegreeOfParallelism");
        maxObserved.Should().BeGreaterThan(1, "the work should actually run in parallel, not serially");
    }

    [Fact]
    [Trait("TC", "TC-RPT-005-50")]
    public async Task RunAllAsync_WhenOneFactoryThrows_Propagates()
    {
        var (factory, parent) = Build(Guid.NewGuid());
        var runner = new ParallelTenantScopeRunner(factory, parent);

        int completed = 0;

        var work = new List<Func<IServiceProvider, CancellationToken, Task<int>>>
        {
            async (sp, ct) => { await Task.Yield(); Interlocked.Increment(ref completed); return 1; },
            (sp, ct) => throw new InvalidOperationException("widget boom"),
            async (sp, ct) => { await Task.Yield(); Interlocked.Increment(ref completed); return 3; },
        };

        var act = async () => await runner.RunAllAsync(work, maxDegreeOfParallelism: 4, CancellationToken.None);

        // Documented choice: the failure propagates (matches DashboardService's no-per-widget-swallow behaviour).
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("widget boom");
        // The non-throwing factories still ran to completion — a throw doesn't corrupt/abort the others' scopes.
        completed.Should().Be(2);
    }
}
