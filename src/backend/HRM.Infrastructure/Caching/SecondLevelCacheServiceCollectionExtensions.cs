using EFCoreSecondLevelCacheInterceptor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Infrastructure.Caching;

/// <summary>
/// P3 (EF second-level cache) registration. Wires the <c>EFCoreSecondLevelCacheInterceptor</c> with:
/// <list type="bullet">
///   <item>a Redis-backed provider when a Redis connection string is configured, otherwise an in-memory
///   provider (mirrors the app's degrade-gracefully pattern — Redis is never a hard dependency);</item>
///   <item>a DYNAMIC, per-request tenant cache-key prefix (<see cref="ICacheTenantKeyProvider"/>) — the
///   load-bearing tenant-isolation guarantee, independent of whether the tenant id is in the SQL;</item>
///   <item>a WHITELIST of slow-changing, read-heavy REFERENCE tables only (never authz / volatile tables);</item>
///   <item>a metrics decorator (<see cref="InstrumentedEFCacheServiceProvider"/>) emitting cache.hit/miss.</item>
/// </list>
/// The registered <c>SecondLevelCacheInterceptor</c> is added to the <c>AppDbContext</c> options in
/// <c>DependencyInjection.AddInfrastructure</c>; it is a COMMAND interceptor and coexists with the existing
/// SaveChanges interceptors (tenant/audit) without conflict.
/// </summary>
public static class SecondLevelCacheServiceCollectionExtensions
{
    /// <summary>
    /// Whitelist fallback (used when <c>Cache:SecondLevelCache:CachedTables</c> is absent/empty). REFERENCE /
    /// master-data tables only: read-dominated and slow-changing. Deliberately EXCLUDES all role/permission
    /// tables (owned by IPermissionCache — never double-cache authz) and every volatile/transactional table
    /// (attendance logs, leave requests/ledger, refresh tokens, audit logs, notifications, workflow/offer/
    /// applicant/payroll rows). Salary component/structure config is also excluded — payroll correctness must
    /// not risk staleness. When unsure, a table is excluded. Snake_case names match the EF configurations.
    /// </summary>
    public static readonly string[] DefaultCachedTables =
    [
        "holiday",
        "leave_types",
        "departments",
        "job_titles",
        "shift",
        "statutory_rule",
        "custom_field_definitions",
        "locations",
    ];

    public static IServiceCollection AddTenantSafeSecondLevelCache(
        this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"];

        var ttlMinutes = configuration.GetValue<int?>("Cache:SecondLevelCache:TtlMinutes") ?? 30;
        var ttl = TimeSpan.FromMinutes(ttlMinutes);

        var cachedTables = configuration.GetSection("Cache:SecondLevelCache:CachedTables").Get<string[]>();
        if (cachedTables is null || cachedTables.Length == 0)
        {
            cachedTables = DefaultCachedTables;
        }

        // The tenant-prefix provider must reach the CURRENT request scope (the library's prefix provider is a
        // root singleton) — IHttpContextAccessor is the AsyncLocal seam for that. Idempotent (TryAdd inside).
        services.AddHttpContextAccessor();
        services.AddSingleton<ICacheTenantKeyProvider, HttpContextCacheTenantKeyProvider>();

        services.AddEFSecondLevelCache(options =>
        {
            if (!string.IsNullOrWhiteSpace(redisConnectionString))
            {
                // Redis outage degrades to a cache miss → DB (the conn string carries abortConnect=false), it
                // never throws the query. Provider caches serialized results under the tenant-prefixed key.
                options.UseStackExchangeRedisCacheProvider(redisConnectionString, ttl);
            }
            else
            {
                options.UseMemoryCacheProvider();
            }

            // 🔴 Tenant-safe cache keys: a dynamic prefix bound to the current request's ITenantContext. The
            // delegate receives the ROOT provider, so it resolves the request-scoped tenant via the singleton
            // ICacheTenantKeyProvider (→ IHttpContextAccessor → RequestServices). Independent of the SQL text.
            options.UseCacheKeyPrefix(sp => sp.GetRequiredService<ICacheTenantKeyProvider>().GetCacheKeyPrefix());

            // RLS/manual-tx paths (SELECT ... FOR UPDATE, tenant purge) wrap queries in explicit transactions;
            // without this nothing inside such a transaction would ever be cached.
            options.AllowCachingWithExplicitTransactions(true);

            // Degrade gracefully: if the cache provider (Redis) becomes unreachable, fall back to a direct DB
            // call instead of throwing, and re-probe availability every 30s. WITHOUT this the library RE-THROWS
            // on a cache-provider error (default UseDbCallsIfCachingProviderIsDown = false), which would fail
            // live queries during a Redis outage — the opposite of the app's never-a-hard-dependency stance.
            options.UseDbCallsIfCachingProviderIsDown(TimeSpan.FromSeconds(30));

            // Cache ONLY queries whose tables are ALL in the whitelist (ContainsOnly) — a join to any
            // non-whitelisted table is not cached. Absolute expiry so reference data can never go stale > TTL.
            options.CacheQueriesContainingTableNames(
                CacheExpirationMode.Absolute, ttl, TableNameComparison.ContainsOnly, cachedTables);

            options.ConfigureLogging(false);
        });

        // Decorate the library's IEFCacheServiceProvider to emit cache.hit/miss + latency metrics. The core
        // registration uses TryAddSingleton(typeof(IEFCacheServiceProvider), concreteType), so we swap that
        // descriptor for a factory that wraps the concrete provider (registered on its own for DI to build).
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IEFCacheServiceProvider));
        if (descriptor?.ImplementationType is { } implementationType)
        {
            services.Remove(descriptor);
            services.AddSingleton(implementationType);
            services.AddSingleton<IEFCacheServiceProvider>(sp =>
                new InstrumentedEFCacheServiceProvider(
                    (IEFCacheServiceProvider)sp.GetRequiredService(implementationType)));
        }

        return services;
    }
}
