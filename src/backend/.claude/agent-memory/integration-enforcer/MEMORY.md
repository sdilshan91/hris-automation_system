# Memory Index

- [IMemoryCache not registered under Redis](imemorycache-not-registered-under-redis.md) — no explicit AddMemoryCache; only the EF 2nd-level cache's memory-provider branch registers it, skipped when Redis is set → GetRequiredService<IMemoryCache> throws in prod.
