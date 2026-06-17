using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="ISessionRevoker"/> (US-ONB-005 FR-7 seam). Logs intent but performs no real access-token
/// denylisting or SignalR disconnect — the Redis JWT denylist + SignalR backplane are not in this stack yet.
/// Offboarding completion still revokes the user's refresh tokens via <c>IAuthService.RevokeAllSessionsAsync</c>
/// (blocking re-auth + refresh); the only gap this seam leaves open is that an already-issued access token
/// works until it expires, because the auth pipeline does not re-check user-active per request.
/// TODO(infra: Redis JWT denylist).
/// </summary>
public sealed class NoOpSessionRevoker : ISessionRevoker
{
    private readonly ILogger<NoOpSessionRevoker> _logger;

    public NoOpSessionRevoker(ILogger<NoOpSessionRevoker> logger) => _logger = logger;

    public Task RevokeActiveSessionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Session revocation requested for user {UserId} in tenant {TenantId} (no-op: Redis JWT denylist " +
            "not wired — refresh tokens are revoked separately; existing access tokens expire naturally).",
            userId, tenantId);
        return Task.CompletedTask;
    }
}
