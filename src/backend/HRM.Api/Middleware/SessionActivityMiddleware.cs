using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace HRM.Api.Middleware;

/// <summary>
/// Middleware that updates the last_active_at timestamp on the user's current session,
/// debounced to at most once per minute per session to minimize DB writes (US-AUTH-009 FR-4, NFR-1).
/// Runs after authentication so ICurrentUser is populated.
/// </summary>
public sealed class SessionActivityMiddleware
{
    private readonly RequestDelegate _next;

    // Tracks the last update time per session ID to enforce the 1-minute debounce window.
    // ConcurrentDictionary is used for thread safety across concurrent requests.
    private static readonly ConcurrentDictionary<Guid, DateTime> _lastUpdateTimes = new();

    private static readonly TimeSpan DefaultDebounceInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The effective debounce window, clamped below the tenant's configured idle timeout so a very short
    /// idle window (the policy validator permits idle = 1 minute) is never defeated by the debounce
    /// (ISSUE-061). Without the clamp, when the fixed 1-minute debounce is >= the idle timeout an actively
    /// used session can still be marked idle-expired because the intermediate requests are debounced out of
    /// advancing <c>last_active_at</c>. Returns <c>min(configured, idleTimeout / 2)</c>; a non-positive idle
    /// timeout falls back to <paramref name="configured"/>.
    /// </summary>
    public static TimeSpan ClampDebounce(TimeSpan configured, int idleTimeoutMinutes)
    {
        if (idleTimeoutMinutes <= 0)
            return configured;

        var half = TimeSpan.FromMinutes(idleTimeoutMinutes) / 2;
        return half < configured ? half : configured;
    }

    public SessionActivityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only process authenticated requests
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await TryUpdateSessionActivityAsync(context);
        }

        await _next(context);
    }

    private static async Task TryUpdateSessionActivityAsync(HttpContext context)
    {
        // Resolve the current session from the refresh token cookie
        var refreshToken = context.Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(refreshToken))
            return;

        var authService = context.RequestServices.GetService<IAuthService>();
        if (authService is null)
            return;

        // Get session ID from the token
        var sessionId = await authService.GetSessionIdFromTokenAsync(refreshToken, context.RequestAborted);
        if (sessionId is null)
            return;

        var now = DateTime.UtcNow;

        // ISSUE-268: RLS-on GUC gap. The fire-and-forget Task runs on a detached fresh scope whose AppDbContext gets
        // no app.current_tenant GUC from the caller, so under RLS-on the RLS-policy-bearing refresh_tokens UPDATE
        // would silently affect 0 rows (IgnoreQueryFilters does NOT bypass DB RLS). Capture the resolved tenant id
        // HERE on the request scope (AsyncLocal/AmbientTenant does not reliably flow into a detached Task.Run) so we
        // can route the update through ITenantJobRunner in the fresh scope. System/unresolved contexts fall back to
        // the legacy direct call (the platform-admin session case, which the runner cannot cleanly tenant-scope).
        var tenantContext = context.RequestServices.GetService<ITenantContext>();
        Guid? scopeTenantId = tenantContext is { IsResolved: true, IsSystemContext: false } tc && tc.TenantId != Guid.Empty
            ? tc.TenantId
            : null;
        var scopeSubdomain = tenantContext?.Subdomain ?? string.Empty;

        // ISSUE-061: clamp the debounce below the tenant's configured idle timeout so a short idle window
        // (the policy validator permits idle = 1 min) is never defeated by the fixed 1-min debounce — the
        // intermediate requests must keep advancing last_active_at. Only resolved tenants have a policy; the
        // system/unresolved path keeps the default debounce.
        var debounceInterval = DefaultDebounceInterval;
        if (scopeTenantId is { } clampTenantId)
        {
            // DF-25: the idle timeout changes rarely but this runs on EVERY authenticated request, defeating the
            // whole point of the activity debounce (minimize DB work). Memoize per-tenant with a short TTL so a
            // settings change still takes effect within ~60s; no invalidation wiring by design (keep it simple).
            var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
            var idleTimeoutMinutes = await cache.GetOrCreateAsync(
                $"session:idle-timeout:{clampTenantId}",
                entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                    return context.RequestServices
                        .GetRequiredService<AppDbContext>().Tenants
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(t => t.Id == clampTenantId)
                        .Select(t => (int?)t.IdleTimeoutMinutes)
                        .FirstOrDefaultAsync(context.RequestAborted);
                });
            if (idleTimeoutMinutes is { } idle)
                debounceInterval = ClampDebounce(DefaultDebounceInterval, idle);
        }

        // Debounce: only update once the (clamped) debounce window has elapsed for this session
        if (_lastUpdateTimes.TryGetValue(sessionId.Value, out var lastUpdate) &&
            (now - lastUpdate) < debounceInterval)
        {
            return;
        }

        // Update the timestamp and record the time
        _lastUpdateTimes[sessionId.Value] = now;

        // Fire-and-forget to avoid adding latency to the request
        // The update is non-critical; if it fails, the next request will retry
        _ = Task.Run(async () =>
        {
            try
            {
                // Create a new scope since this runs outside the request scope
                var scopeFactory = context.RequestServices.GetService<IServiceScopeFactory>();
                if (scopeFactory is null) return;

                using var scope = scopeFactory.CreateScope();
                var scopedAuthService = scope.ServiceProvider.GetRequiredService<IAuthService>();

                if (scopeTenantId is { } tenantId)
                {
                    // Route through the runner so the GUC is set on the fresh scope's AppDbContext (the same scoped
                    // instance AuthService uses). No-ops under Rls:Enabled=false / InMemory — behaviour unchanged.
                    var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
                    await runner.RunForTenantAsync(
                        tenantId, scopeSubdomain,
                        ct => scopedAuthService.UpdateSessionActivityAsync(sessionId.Value, ct),
                        CancellationToken.None);
                }
                else
                {
                    await scopedAuthService.UpdateSessionActivityAsync(sessionId.Value);
                }
            }
            catch
            {
                // Swallow — session activity tracking is best-effort
            }
        });
    }

    /// <summary>
    /// Evicts stale entries from the debounce cache (called by cleanup job or on a timer).
    /// </summary>
    public static void PurgeStaleEntries()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        foreach (var kvp in _lastUpdateTimes)
        {
            if (kvp.Value < cutoff)
            {
                _lastUpdateTimes.TryRemove(kvp.Key, out _);
            }
        }
    }
}
