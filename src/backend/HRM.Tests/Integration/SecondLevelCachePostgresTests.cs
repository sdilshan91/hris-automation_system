// ============================================================================
// P3 (EF second-level cache) — real-Postgres tests for the EFCoreSecondLevelCacheInterceptor wiring.
//
// HARNESS = real Postgres via Testcontainers (NOT InMemory), same rationale/pattern as
// AppraisalCycleScopePostgresTests: the interceptor is a relational DbCommand interceptor and is INERT on the
// InMemory provider, so a genuine test of caching/invalidation/prefixing must run against real Postgres. The
// CACHE provider itself is the in-memory one (behavior is provider-independent — the tenant prefix + table
// whitelist + invalidation logic are identical for Redis) so the tests need no Redis.
//
// The tenant-isolation test (WhitelistedQuery_DifferentCacheKeyPrefix_IsolatesResults) is the load-bearing one:
// it holds the DbContext's tenant (hence the SQL + parameters) CONSTANT and flips ONLY the cache-key prefix,
// then proves that a different prefix forces a cache MISS (fresh DB read) — i.e. results never bleed across
// prefixes. If the prefix were broken (constant), the second read would be served the first tenant's stale
// cached rows and the assertion would fail. This is exactly the collision RLS would create once the tenant id
// leaves the SQL, reproduced deterministically here.
// ============================================================================

using System.Diagnostics.Metrics;
using EFCoreSecondLevelCacheInterceptor;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure;
using HRM.Infrastructure.Caching;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class SecondLevelCachePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly MutableTenantContext _tc = new();
    private readonly MutableCacheKeyProvider _keyProvider = new();
    private ICurrentUser _cu = null!;
    private ServiceProvider _provider = null!;

    // The DbContext's tenant context (drives the global query filter). Kept fixed across a test so the SQL +
    // parameters stay identical while we vary only the CACHE prefix.
    private sealed class MutableTenantContext : ITenantContext
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
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status, string? plan = null,
            IReadOnlyCollection<string>? enabledModules = null, string? logoUrl = null, string? primaryColor = null)
            => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    // Test double for the cache-key prefix source — lets us drive the prefix deterministically without HTTP.
    private sealed class MutableCacheKeyProvider : ICacheTenantKeyProvider
    {
        public string Prefix { get; set; } = "t:test:";
        public string GetCacheKeyPrefix() => Prefix;
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _tc.TenantId = _tenantId;
        _cu = Substitute.For<ICurrentUser>();
        _cu.IsAuthenticated.Returns(true);
        _cu.UserId.Returns(Guid.NewGuid());
        _cu.Email.Returns("hr@acme.com");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Blank Redis => in-memory cache provider (see extension). Default TTL is fine for the tests.
                ["Cache:SecondLevelCache:TtlMinutes"] = "30",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(_tc);
        services.AddSingleton(_cu);

        // Real cache wiring (memory provider + tenant prefix + whitelist + metrics decorator).
        services.AddTenantSafeSecondLevelCache(configuration);
        // Override the prefix source AFTER the extension so we control it deterministically (last wins).
        services.AddSingleton<ICacheTenantKeyProvider>(_keyProvider);

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(_postgres.GetConnectionString(),
                    n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(
                    new TenantInterceptor(_tc),
                    new AuditInterceptor(_cu),
                    sp.GetRequiredService<SecondLevelCacheInterceptor>());
        });

        _provider = services.BuildServiceProvider();

        await using var db = NoCacheDb();
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        // Two whitelisted (holiday) rows to start.
        db.Holidays.Add(NewHoliday("New Year"));
        db.Holidays.Add(NewHoliday("Republic Day"));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private int _dateSeq;

    // Each holiday gets a DISTINCT date so the (tenant, date, no-location) unique index is never violated.
    private Holiday NewHoliday(string name) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, Name = name,
        Date = new DateOnly(2030, 1, 1).AddDays(Interlocked.Increment(ref _dateSeq)),
        Type = HolidayType.Public, IsActive = true,
    };

    // A CACHE-ENABLED context from DI (fresh scope each call ⇒ no first-level identity-map interference).
    private AppDbContext CachedDb(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // A context WITHOUT the second-level cache interceptor — used for out-of-band writes that must NOT trigger
    // the interceptor's auto-invalidation (so we can prove the cache is actually serving a prior result).
    private AppDbContext NoCacheDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(),
                n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(_tc), new AuditInterceptor(_cu))
            .Options, _tc);

    private async Task<int> HolidayCountAsync()
    {
        using var scope = _provider.CreateScope();
        await using var db = CachedDb(scope);
        return (await db.Holidays.ToListAsync()).Count;
    }

    // ── LOAD-BEARING: the tenant prefix isolates cached results ──────────────
    [Trait("TC", "TC-INF-CACHE-002")]
    [Fact]
    public async Task WhitelistedQuery_DifferentCacheKeyPrefix_IsolatesResults()
    {
        _keyProvider.Prefix = "t:AAAA:";
        (await HolidayCountAsync()).Should().Be(2, "first read populates the cache under prefix t:AAAA:");

        // Out-of-band insert (no cache invalidation) ⇒ DB now has 3, but the cache still holds 2.
        await using (var db = NoCacheDb())
        {
            db.Holidays.Add(NewHoliday("Diwali"));
            await db.SaveChangesAsync();
        }

        // Same prefix ⇒ same cache key ⇒ served the STALE cached 2 (proves the cache is actually serving).
        _keyProvider.Prefix = "t:AAAA:";
        (await HolidayCountAsync()).Should().Be(2, "identical prefix + SQL is served from cache");

        // DIFFERENT prefix, identical SQL/parameters ⇒ different cache key ⇒ MISS ⇒ fresh DB read of 3.
        // If the prefix were broken (constant), this would hit t:AAAA:'s entry and return the stale 2 — the
        // exact cross-tenant leak the prefix prevents. This assertion fails loudly in that case.
        _keyProvider.Prefix = "t:BBBB:";
        (await HolidayCountAsync()).Should().Be(3, "a different tenant prefix must not see another prefix's cached rows");
    }

    // ── Cache HIT on an identical same-prefix query (asserted via the cache.hit metric) ──
    [Trait("TC", "TC-INF-CACHE-003")]
    [Fact]
    public async Task WhitelistedQuery_SecondIdenticalCall_IsServedFromCache_AndRecordsHit()
    {
        _keyProvider.Prefix = "t:HIT:";
        long hits = 0, misses = 0;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == HrmCacheMetrics.MeterName && instrument.Name == "hrm.cache.requests")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "cache.hit" && tag.Value is bool hit)
                {
                    if (hit) Interlocked.Add(ref hits, measurement);
                    else Interlocked.Add(ref misses, measurement);
                }
            }
        });
        listener.Start();

        await HolidayCountAsync(); // miss ⇒ executes ⇒ inserts into cache
        await HolidayCountAsync(); // identical prefix + SQL ⇒ hit

        listener.Dispose();

        misses.Should().BeGreaterThanOrEqualTo(1, "the first read is a cache miss");
        hits.Should().BeGreaterThanOrEqualTo(1, "the second identical read is served from cache (a hit)");
    }

    // ── SaveChanges on a whitelisted table auto-invalidates the cache (no stale reads) ──
    [Trait("TC", "TC-INF-CACHE-004")]
    [Fact]
    public async Task SaveChanges_OnWhitelistedTable_InvalidatesCache()
    {
        _keyProvider.Prefix = "t:INV:";
        (await HolidayCountAsync()).Should().Be(2);

        // Write through a CACHE-ENABLED context ⇒ the interceptor detects the changed 'holiday' table and
        // invalidates its cached queries on SaveChanges.
        using (var scope = _provider.CreateScope())
        await using (var db = CachedDb(scope))
        {
            db.Holidays.Add(NewHoliday("Christmas"));
            await db.SaveChangesAsync();
        }

        (await HolidayCountAsync()).Should().Be(3, "SaveChanges on a whitelisted table must invalidate its cache");
    }

    // ── A non-whitelisted (volatile) table is NOT cached ──
    [Trait("TC", "TC-INF-CACHE-005")]
    [Fact]
    public async Task NonWhitelistedTable_IsNotCached()
    {
        _keyProvider.Prefix = "t:NOCACHE:";

        async Task<int> TenantCountAsync()
        {
            using var scope = _provider.CreateScope();
            await using var db = CachedDb(scope);
            return await db.Tenants.CountAsync();
        }

        (await TenantCountAsync()).Should().Be(1);

        // Out-of-band insert of another (non-whitelisted) tenant row, no invalidation.
        await using (var db = NoCacheDb())
        {
            db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Subdomain = "beta", Name = "Beta" });
            await db.SaveChangesAsync();
        }

        // 'tenants' is not whitelisted ⇒ never cached ⇒ the fresh row is visible immediately.
        (await TenantCountAsync()).Should().Be(2, "a non-whitelisted table must not be cached");
    }
}
