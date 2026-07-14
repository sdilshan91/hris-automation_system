namespace HRM.Application.Common.Interfaces;

/// <summary>
/// The ONE source of the my-tenants cache key so the read/write in
/// <c>AuthService.GetMyTenantsAsync</c> and every invalidation site target the EXACT same key
/// (BUG-116). The entry holds a user's tenant memberships + roles (TTL 5 min).
/// </summary>
public static class MyTenantsCacheKey
{
    public static string For(Guid userId) => $"user:{userId}:tenants";
}

/// <summary>
/// Invalidates a user's cached my-tenants entry (see <see cref="MyTenantsCacheKey"/>) on any
/// membership/role mutation, so authorization data is never served stale for up to the 5-min TTL
/// (BUG-116). <b>Fail-soft:</b> a Redis blip must never break the membership mutation — the
/// implementation logs and swallows cache errors (mirrors the BUG-121 fail-soft handling in
/// <c>AuthService.GetMyTenantsAsync</c>); the stale entry then expires on its own TTL.
/// </summary>
public interface IMyTenantsCache
{
    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default);
}
