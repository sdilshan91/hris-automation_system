using FluentAssertions;
using HRM.Infrastructure.Caching;
using HRM.Infrastructure.Multitenancy;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

/// <summary>
/// RLS increment 1 — the LOAD-BEARING prerequisite test. Proves that the production <see cref="TenantContext"/>
/// publishes to the AsyncLocal <see cref="AmbientTenant"/>, so the (root-singleton) cache key-prefix provider
/// resolves a TENANT-SCOPED prefix even with NO HttpContext — i.e. exactly the background/Hangfire-job and
/// startup path that previously collapsed to the shared <c>nohttp:</c> bucket. Under RLS (increment 2) that
/// shared bucket would become a cross-tenant cache collision; this closes it before RLS lands.
///
/// <para>These tests deliberately use the real <see cref="TenantContext"/> and the real
/// <see cref="AmbientTenantCacheKeyProvider"/> with NO <c>IHttpContextAccessor</c> in play — a faithful stand-in
/// for a job/startup context.</para>
/// </summary>
public sealed class AmbientTenantCacheKeyProviderTests
{
    private static readonly AmbientTenantCacheKeyProvider Provider = new();

    [Trait("TC", "TC-INF-CACHE-006")]
    [Fact]
    public void SetTenant_NoHttpContext_YieldsTenantScopedPrefix_NotNoHttp()
    {
        AmbientTenant.Clear();
        var tid = Guid.NewGuid();

        // A job/startup flow: no HttpContext, just SetTenant on the scoped context (exactly what every Hangfire
        // job does at its top).
        var ctx = new TenantContext();
        ctx.SetTenant(tid, "acme", HRM.Domain.Entities.TenantStatus.Active);

        var prefix = Provider.GetCacheKeyPrefix();

        prefix.Should().Be($"t:{tid}:", "a no-HttpContext job/startup flow must cache under a tenant-scoped prefix");
        prefix.Should().NotBe(CacheTenantPrefix.NoHttpContext, "the shared nohttp: bucket is exactly the leak this fixes");
    }

    [Trait("TC", "TC-INF-CACHE-006")]
    [Fact]
    public void SetSystemContext_NoHttpContext_YieldsSystemPrefix()
    {
        AmbientTenant.Clear();

        var ctx = new TenantContext();
        ctx.SetSystemContext();

        Provider.GetCacheKeyPrefix().Should().Be(CacheTenantPrefix.System);
    }

    [Trait("TC", "TC-INF-CACHE-006")]
    [Fact]
    public void TwoTenants_OnSameFlow_ProduceDistinctPrefixes()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var ctx = new TenantContext();

        ctx.SetTenant(a, "acme", HRM.Domain.Entities.TenantStatus.Active);
        var prefixA = Provider.GetCacheKeyPrefix();

        ctx.SetTenant(b, "beta", HRM.Domain.Entities.TenantStatus.Active);
        var prefixB = Provider.GetCacheKeyPrefix();

        prefixA.Should().Be($"t:{a}:");
        prefixB.Should().Be($"t:{b}:");
        prefixA.Should().NotBe(prefixB, "switching the ambient tenant must switch the cache namespace");
    }

    [Trait("TC", "TC-INF-CACHE-006")]
    [Fact]
    public void NoAmbientSet_FallsBackToUnresolved_NeverTenantCrossing()
    {
        AmbientTenant.Clear();

        var prefix = Provider.GetCacheKeyPrefix();

        prefix.Should().Be(CacheTenantPrefix.Unresolved);
        prefix.Should().NotStartWith("t:");
    }

    [Trait("TC", "TC-INF-CACHE-006")]
    [Fact]
    public async Task Ambient_FlowsDownAsyncChain_PrefixStillResolves()
    {
        var tid = Guid.NewGuid();
        var ctx = new TenantContext();
        ctx.SetTenant(tid, "acme", HRM.Domain.Entities.TenantStatus.Active);

        // AsyncLocal must flow into awaited continuations (the whole point — a job's async work keeps the tenant).
        async Task<string> ResolveAfterAwaitAsync()
        {
            await Task.Yield();
            await Task.Delay(1);
            return Provider.GetCacheKeyPrefix();
        }

        (await ResolveAfterAwaitAsync()).Should().Be($"t:{tid}:", "the ambient tenant must flow down the async context");
    }
}
