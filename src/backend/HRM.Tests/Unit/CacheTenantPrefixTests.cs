using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Caching;

namespace HRM.Tests.Unit;

/// <summary>
/// P3 (EF second-level cache) — deterministic guard on the LOAD-BEARING tenant-isolation primitive
/// (<see cref="CacheTenantPrefix"/>). This is independent of the SQL text, so it is the mechanism that keeps
/// the query-result cache tenant-safe once RLS moves the tenant id out of the SQL. It genuinely fails if the
/// mapping is broken (e.g. two tenants collapse to one prefix). Complements the Postgres cache tests.
/// </summary>
public sealed class CacheTenantPrefixTests
{
    private sealed class StubTenantContext : ITenantContext
    {
        public Guid TenantId { get; init; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext { get; init; }
        public bool IsResolved { get; init; }
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status, string? plan = null,
            IReadOnlyCollection<string>? enabledModules = null, string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    [Trait("TC", "TC-INF-CACHE-001")]
    [Fact]
    public void TwoDifferentTenants_ProduceDistinctNeverSharedPrefixes()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var prefixA = CacheTenantPrefix.For(new StubTenantContext { TenantId = a, IsResolved = true });
        var prefixB = CacheTenantPrefix.For(new StubTenantContext { TenantId = b, IsResolved = true });

        prefixA.Should().Be($"t:{a}:");
        prefixB.Should().Be($"t:{b}:");
        prefixA.Should().NotBe(prefixB, "each tenant must occupy its own cache-key namespace");
    }

    [Trait("TC", "TC-INF-CACHE-001")]
    [Fact]
    public void SystemContext_UsesItsOwnPrefix_NeverSharedWithATenant()
    {
        var system = CacheTenantPrefix.For(new StubTenantContext { IsSystemContext = true, IsResolved = true });

        system.Should().Be(CacheTenantPrefix.System);
        system.Should().NotStartWith("t:");
    }

    [Trait("TC", "TC-INF-CACHE-001")]
    [Fact]
    public void UnresolvedOrEmptyTenant_UsesDistinctSentinel_NeverSharedWithAResolvedTenant()
    {
        var unresolved = CacheTenantPrefix.For(new StubTenantContext { IsResolved = false });
        var emptyId = CacheTenantPrefix.For(new StubTenantContext { TenantId = Guid.Empty, IsResolved = true });

        unresolved.Should().Be(CacheTenantPrefix.Unresolved);
        emptyId.Should().Be(CacheTenantPrefix.Unresolved);
        unresolved.Should().NotBe(CacheTenantPrefix.System);
    }

    [Trait("TC", "TC-INF-CACHE-001")]
    [Fact]
    public void NoTenantContext_NonHttpFlow_UsesDistinctNoHttpSentinel()
    {
        CacheTenantPrefix.For(null).Should().Be(CacheTenantPrefix.NoHttpContext);
    }
}
