// ============================================================================
// AUTH-007 — tenant lifecycle invalidates the subdomain-resolution cache (FR-9).
//
// TenantResolutionMiddleware caches subdomain→tenant resolution under key
// "t:subdomain:{subdomain.ToLowerInvariant()}" (5-min TTL). Before this change a
// suspend/terminate/reactivate/restore did not touch that cache, so a suspended
// tenant kept resolving as Active until the TTL elapsed — a login-block bypass
// window. TenantLifecycleService now takes an optional IDistributedCache and, on
// each transition, removes that key.
//
// Seam: a real spy IDistributedCache records every RemoveAsync key. We drive the
// genuine service against InMemory EF, then assert the persisted status changed
// AND the exact lowercased cache key was evicted. The subdomain is seeded as
// mixed-case "Acme" specifically to pin the ToLowerInvariant() contract — the
// evicted key must be "t:subdomain:acme", the same shape the middleware writes.
//
// Why it fails pre-fix: the ctor had no cache param and no eviction call, so the
// spy records zero keys and every assertion fails.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Tenants.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class TenantSubdomainCacheInvalidationTests
{
    private const string Subdomain = "Acme";               // mixed case on purpose
    private const string ExpectedKey = "t:subdomain:acme"; // must be lowercased
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    // Binds @TC-ADM-CACHE-001.
    [Fact]
    public async Task TenantSuspend_InvalidatesSubdomainCache_AUTH007()
    {
        await SeedTenantAsync(TenantStatus.Active);
        var spy = new SpyDistributedCache();

        var result = await CreateService(spy).SuspendAsync(
            new SuspendTenantInput(_tenantId, "Suspended for AUTH-007 cache-invalidation regression."), default);

        result.IsSuccess.Should().BeTrue();
        StatusOf().Should().Be(TenantStatus.Suspended, "the transition must actually persist");
        spy.RemovedKeys.Should().ContainSingle().Which.Should().Be(ExpectedKey,
            "suspend must evict the cached (Active) subdomain resolution so the login block takes effect immediately (FR-9)");
    }

    // Binds @TC-ADM-CACHE-001.
    [Fact]
    public async Task TenantTerminate_InvalidatesSubdomainCache_AUTH007()
    {
        await SeedTenantAsync(TenantStatus.Active);
        var spy = new SpyDistributedCache();

        var result = await CreateService(spy).TerminateAsync(
            new TerminateTenantInput(_tenantId, "Terminated for AUTH-007 cache-invalidation regression.", null), default);

        result.IsSuccess.Should().BeTrue();
        StatusOf().Should().Be(TenantStatus.Terminating);
        spy.RemovedKeys.Should().ContainSingle().Which.Should().Be(ExpectedKey,
            "termination must evict the cached (Active) subdomain resolution (FR-9)");
    }

    // Binds @TC-ADM-CACHE-001.
    [Fact]
    public async Task TenantReactivate_InvalidatesSubdomainCache_AUTH007()
    {
        // Reactivate is only valid from Suspended (BR-1); seed accordingly.
        await SeedTenantAsync(TenantStatus.Suspended, suspended: true);
        var spy = new SpyDistributedCache();

        var result = await CreateService(spy).ReactivateAsync(new ReactivateTenantInput(_tenantId), default);

        result.IsSuccess.Should().BeTrue();
        StatusOf().Should().Be(TenantStatus.Active);
        spy.RemovedKeys.Should().ContainSingle().Which.Should().Be(ExpectedKey,
            "reactivation must refresh the cached resolution so the tenant resolves as Active immediately (FR-9)");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private TenantLifecycleService CreateService(IDistributedCache cache) => new(
        CreateDbContext(),
        Substitute.For<ICurrentUser>(),
        Substitute.For<ITenantLifecycleNotificationService>(),
        new ConfigurationBuilder().Build(),
        Substitute.For<ILogger<TenantLifecycleService>>(),
        scheduler: null,
        cache: cache);

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantId, _dbName);

    private TenantStatus StatusOf()
    {
        using var db = CreateDbContext();
        return db.Tenants.IgnoreQueryFilters().Single(t => t.Id == _tenantId).Status;
    }

    private async Task SeedTenantAsync(TenantStatus status, bool suspended = false)
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Name = "Acme Corp",
            Subdomain = Subdomain,
            Status = status,
            SuspendedAt = suspended ? DateTime.UtcNow : null,
            SuspendedReason = suspended ? "prior suspension" : null,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Minimal spy IDistributedCache: it is a no-op store that records the keys passed to RemoveAsync,
    /// which is the only method under test. Any other member is never exercised by the eviction path.
    /// </summary>
    private sealed class SpyDistributedCache : IDistributedCache
    {
        public List<string> RemovedKeys { get; } = new();

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            RemovedKeys.Add(key);
            return Task.CompletedTask;
        }

        public void Remove(string key) => RemovedKeys.Add(key);

        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }
}
