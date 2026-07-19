---
name: imemorycache-not-registered-under-redis
description: IMemoryCache is NOT unconditionally registered in this app — it only appears via the EF second-level cache's memory-provider branch, which is skipped when Redis is configured. GetRequiredService<IMemoryCache> throws in prod.
metadata:
  type: project
---

There is **no explicit `AddMemoryCache()`** anywhere in `src/backend`. The only registrar of
`IMemoryCache` is the EF second-level cache: `AddTenantSafeSecondLevelCache`
(`HRM.Infrastructure/Caching/SecondLevelCacheServiceCollectionExtensions.cs`) →
`AddEFSecondLevelCache(o => o.UseMemoryCacheProvider())`. Verified by probe: `AddEFSecondLevelCache`
registers `IMemoryCache` **only on the `UseMemoryCacheProvider()` branch**. On the
`UseStackExchangeRedisCacheProvider(...)` branch it registers **neither** `IMemoryCache` nor a
descriptor for it.

That branch is selected by `ConnectionStrings:Redis` (or `Redis:ConnectionString`):
- **base `appsettings.json`** sets `Redis` = `localhost:6379,...` → Redis branch → **`IMemoryCache` absent**.
- **`appsettings.Development.json`** blanks `Redis` → memory branch → `IMemoryCache` present.

`IDistributedCache`, by contrast, IS always registered (`AddDistributedMemoryCache` fallback at
`DependencyInjection.cs:792`, or Redis) — so prefer `IDistributedCache` for new per-request caching,
or add an explicit `builder.Services.AddMemoryCache()` in `Program.cs`.

**Why:** any new `GetRequiredService<IMemoryCache>()` (e.g. DF-25 in `SessionActivityMiddleware`) passes
dev + tests (Redis blank) but throws `InvalidOperationException` on every authenticated request in any
Redis-configured environment. Same masked-by-config class as [[green-suite-is-not-evidence]].

**How to apply:** when auditing wiring, treat `IMemoryCache` as NOT registered by default. Flag any
`GetRequiredService<IMemoryCache>()`/`GetService<IMemoryCache>()` unless an explicit `AddMemoryCache()`
was added, and check the code path is only exercised under the Development (Redis-blank) config.
