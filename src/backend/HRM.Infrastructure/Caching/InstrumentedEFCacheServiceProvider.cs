using System.Diagnostics;
using EFCoreSecondLevelCacheInterceptor;

namespace HRM.Infrastructure.Caching;

/// <summary>
/// P3 cache observability — a transparent decorator over the library's <see cref="IEFCacheServiceProvider"/>
/// (Redis or in-memory) that records <c>cache.hit</c>/<c>cache.miss</c> + read latency to
/// <see cref="HrmCacheMetrics"/> on every <see cref="GetValue"/>. All other operations delegate straight
/// through. A <c>null</c> result from the inner provider is a cache MISS; a non-null result is a HIT (an
/// <see cref="EFCachedData"/> with <c>IsNull=true</c> is still a hit — it means "the cached query result was
/// null"). Only the outcome + latency are measured; cache keys/values are never inspected or tagged.
/// </summary>
public sealed class InstrumentedEFCacheServiceProvider : IEFCacheServiceProvider
{
    private readonly IEFCacheServiceProvider _inner;

    public InstrumentedEFCacheServiceProvider(IEFCacheServiceProvider inner) => _inner = inner;

    public EFCachedData? GetValue(EFCacheKey cacheKey, EFCachePolicy cachePolicy)
    {
        var start = Stopwatch.GetTimestamp();
        EFCachedData? result = null;
        try
        {
            result = _inner.GetValue(cacheKey, cachePolicy);
            return result;
        }
        finally
        {
            HrmCacheMetrics.RecordRead(
                hit: result is not null,
                elapsedMilliseconds: Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    public void InsertValue(EFCacheKey cacheKey, EFCachedData? value, EFCachePolicy cachePolicy)
        => _inner.InsertValue(cacheKey, value, cachePolicy);

    public void InvalidateCacheDependencies(EFCacheKey cacheKey)
        => _inner.InvalidateCacheDependencies(cacheKey);

    public void ClearAllCachedEntries() => _inner.ClearAllCachedEntries();
}
