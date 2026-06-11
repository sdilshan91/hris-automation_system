using HRM.Application.Common.Interfaces;
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

    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMinutes(1);

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

        // Debounce: only update if more than 1 minute has passed since last update for this session
        if (_lastUpdateTimes.TryGetValue(sessionId.Value, out var lastUpdate) &&
            (now - lastUpdate) < DebounceInterval)
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
                await scopedAuthService.UpdateSessionActivityAsync(sessionId.Value);
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
