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
        // Reference / org
        "holiday",
        "leave_types",
        "departments",
        "job_titles",
        "shift",
        "statutory_rule",
        "custom_field_definitions",
        "locations",
        // Policy / config
        "attendance_settings",
        "late_policy",
        "leave_entitlement_rules",
        "leave_entitlement_overrides",
        "shift_rotation_step",
        "employee_shift",
        // Catalogs / templates
        "notification_template",
        "system_notification_template",
        "onboarding_checklist_template",
        "onboarding_template_task",
        "exit_interview_template",
        "exit_interview_question",
        "scheduled_report_config",
        "recommendation_rule",
        // Plan / statutory. tax_slab + social_security_rule auto-evict on SaveChanges; the only staleness
        // path is a bulk/raw-SQL rate edit that bypasses the interceptor — statutory rates in this app are
        // updated via services → SaveChanges, so this is safe. Revisit if rates ever move to bulk updates.
        "subscription_plans",
        "plan_limit_overrides",
        "tax_slab",
        "social_security_rule",
        // Workflow design-time (definitions/steps are slow-changing config; runtime instances are a
        // separate table and are NOT cached).
        "workflow_definitions",
        "workflow_steps",
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

        // The tenant-prefix provider must reach the CURRENT tenant even though the library's prefix provider is
        // a root singleton. The AsyncLocal-backed AmbientTenant is that seam: TenantContext.SetTenant/
        // SetSystemContext publishes to it on BOTH the HTTP path (TenantResolutionMiddleware) and every
        // Hangfire job, so HTTP requests AND background/startup queries alike get a tenant-scoped prefix.
        services.AddSingleton<ICacheTenantKeyProvider, AmbientTenantCacheKeyProvider>();

        services.AddEFSecondLevelCache(options =>
        {
            if (!string.IsNullOrWhiteSpace(redisConnectionString))
            {
                // Redis outage degrades to a cache miss → DB (the conn string carries abortConnect=false), it
                // never throws the query. Provider caches serialized results under the tenant-prefixed key.
                // NOTE (Redis command-spans / OTel): this provider builds and owns its OWN private
                // IConnectionMultiplexer from the connection string — the package (5.3.13) neither exposes nor
                // accepts a shared one — so its Redis commands are NOT covered by the shared-multiplexer
                // AddRedisInstrumentation (which only instruments IDistributedCache + the SignalR backplane).
                // Covering it would need a custom IEFCacheServiceProvider on the shared multiplexer. See ISSUE-274.
                options.UseStackExchangeRedisCacheProvider(redisConnectionString, ttl);
            }
            else
            {
                options.UseMemoryCacheProvider();
            }

            // 🔴 Tenant-safe cache keys: a dynamic prefix bound to the current async flow's ambient tenant. The
            // delegate receives the ROOT provider, so it resolves the tenant via the singleton
            // ICacheTenantKeyProvider (→ AsyncLocal AmbientTenant). Independent of the SQL text, and populated
            // on both HTTP and background-job flows.
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
